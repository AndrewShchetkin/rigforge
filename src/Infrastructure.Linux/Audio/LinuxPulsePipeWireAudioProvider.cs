using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using RigForge.Core.Interfaces;

namespace RigForge.Infrastructure.Linux.Audio;

/// <summary>
/// Loopback levels from the default sink monitor through <c>libpulse-simple</c>.
/// The same library ships with PulseAudio and with PipeWire's <c>pipewire-pulse</c>.
/// Capture starts on the first <see cref="ReadSpectrum"/> call and is released after five idle seconds.
/// A missing library sets <see cref="IsSupported"/> false and never throws.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class LinuxPulsePipeWireAudioProvider(ILogger<LinuxPulsePipeWireAudioProvider> logger) : IAudioLevelProvider
{
    private const int FftSize = 256;
    private const int Hop = 256;
    private const int BandCount = 16;
    private const int StreamRecord = 2;
    private const int SampleFloat32Le = 5;
    private const int SampleRate = 48_000;
    private const int Channels = 2;
    private const int FramesPerRead = 256;

    private static readonly TimeSpan IdleLimit = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private static readonly bool NativeAvailable = OperatingSystem.IsLinux() && NativeLibrary.TryLoad("libpulse-simple.so.0", out _);

    private readonly Lock _lifecycle = new();
    private readonly Lock _levels = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly float[] _hann = CreateWindow();
    private readonly float[] _ring = new float[FftSize];
    private readonly float[] _real = new float[FftSize];
    private readonly float[] _imag = new float[FftSize];
    private readonly float[] _bands = new float[BandCount];
    private readonly int[] _bandStart = new int[BandCount];
    private readonly int[] _bandEnd = new int[BandCount];
    private readonly byte[] _capture = new byte[FramesPerRead * Channels * sizeof(float)];

    private Thread? _thread;
    private long _lastRead;
    private float _rms;
    private int _ringPos;
    private int _filled;
    private int _sinceHop;
    private bool _bandsReady;
    private bool _loggedFailure;
    private bool _loggedStart;
    private volatile bool _running;
    private volatile bool _disposed;

    public bool IsSupported => NativeAvailable;

    public float ReadSpectrum(Span<float> bands)
    {
        if (_disposed || !IsSupported)
        {
            bands.Clear();
            return 0f;
        }

        Volatile.Write(ref _lastRead, Stopwatch.GetTimestamp());
        EnsureStarted();
        return Copy(bands);
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_lifecycle)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            thread = _thread;
        }

        _stop.Cancel();
        if (thread is not null && thread.IsAlive && !thread.Join(TimeSpan.FromSeconds(2)))
        {
            return;
        }

        _stop.Dispose();
        GC.SuppressFinalize(this);
    }

    private void EnsureStarted()
    {
        if (_running)
        {
            return;
        }

        lock (_lifecycle)
        {
            if (_disposed || _thread is not null)
            {
                return;
            }

            if (!_bandsReady)
            {
                BuildBands(SampleRate);
                _bandsReady = true;
            }

            Volatile.Write(ref _lastRead, Stopwatch.GetTimestamp());
            _running = true;
            _thread = new Thread(CaptureLoop)
            {
                IsBackground = true,
                Name = "RigForge Pulse loopback",
            };
            _thread.Start();
        }
    }

    private float Copy(Span<float> bands)
    {
        lock (_levels)
        {
            var count = Math.Min(bands.Length, BandCount);
            for (var i = 0; i < count; i++)
            {
                bands[i] = _bands[i];
            }

            for (var i = count; i < bands.Length; i++)
            {
                bands[i] = 0f;
            }

            return _rms;
        }
    }

    private unsafe void CaptureLoop()
    {
        var simple = IntPtr.Zero;
        try
        {
            var token = _stop.Token;
            while (!token.IsCancellationRequested && !_disposed && !IsIdle())
            {
                try
                {
                    if (simple == IntPtr.Zero)
                    {
                        simple = OpenCapture();
                        if (simple == IntPtr.Zero)
                        {
                            if (token.WaitHandle.WaitOne(RetryDelay) || IsIdle())
                            {
                                break;
                            }

                            continue;
                        }

                        if (!_loggedStart)
                        {
                            _loggedStart = true;
                            LogCaptureStarted(SampleRate, Channels);
                        }
                    }

                    var error = 0;
                    fixed (byte* buffer = _capture)
                    {
                        if (Read(simple, buffer, (nuint)_capture.Length, &error) < 0)
                        {
                            Free(ref simple);
                            ZeroLevels();
                            if (token.WaitHandle.WaitOne(RetryDelay) || IsIdle())
                            {
                                break;
                            }

                            continue;
                        }
                    }

                    Consume();
                }
                catch (Exception ex) when (!token.IsCancellationRequested && !_disposed)
                {
                    Free(ref simple);
                    ZeroLevels();
                    if (!_loggedFailure)
                    {
                        _loggedFailure = true;
                        LogCaptureUnavailable(ex);
                    }

                    if (token.WaitHandle.WaitOne(RetryDelay) || IsIdle())
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            Free(ref simple);
            ZeroLevels();
            lock (_lifecycle)
            {
                _thread = null;
                _running = false;
            }
        }
    }

    private unsafe IntPtr OpenCapture()
    {
        var spec = new SampleSpec
        {
            Format = SampleFloat32Le,
            Rate = SampleRate,
            Channels = Channels,
        };
        var error = 0;
        var handle = New(
            null,
            "RigForge",
            StreamRecord,
            "@DEFAULT_MONITOR@",
            "Loopback",
            ref spec,
            IntPtr.Zero,
            IntPtr.Zero,
            &error);
        if (handle == IntPtr.Zero && !_loggedFailure)
        {
            _loggedFailure = true;
            LogCaptureUnavailable(new InvalidOperationException($"pa_simple_new failed ({error})."));
        }

        return handle;
    }

    private void Consume()
    {
        for (var frame = 0; frame < FramesPerRead; frame++)
        {
            var offset = frame * Channels * sizeof(float);
            var left = BitConverter.ToSingle(_capture, offset);
            var right = BitConverter.ToSingle(_capture, offset + sizeof(float));
            var sample = (left + right) * 0.5f;
            if (!float.IsFinite(sample))
            {
                sample = 0f;
            }

            Push(sample);
        }
    }

    private void Push(float sample)
    {
        if (sample > 1f)
        {
            sample = 1f;
        }
        else if (sample < -1f)
        {
            sample = -1f;
        }

        _ring[_ringPos] = sample;
        _ringPos = (_ringPos + 1) & (FftSize - 1);
        if (_filled < FftSize)
        {
            _filled++;
        }

        _sinceHop++;
        if (_filled == FftSize && _sinceHop >= Hop)
        {
            _sinceHop = 0;
            Analyze();
        }
    }

    private void Analyze()
    {
        var oldest = _ringPos;
        float sum = 0f;
        float sumSq = 0f;
        for (var i = 0; i < FftSize; i++)
        {
            var sample = _ring[(oldest + i) & (FftSize - 1)];
            sum += sample;
            sumSq += sample * sample;
        }

        var mean = sum / FftSize;
        for (var i = 0; i < FftSize; i++)
        {
            var sample = _ring[(oldest + i) & (FftSize - 1)];
            _real[i] = (sample - mean) * _hann[i];
            _imag[i] = 0f;
        }

        Transform(_real, _imag);

        var rms = MathF.Min(1f, MathF.Sqrt(sumSq / FftSize) * 1.41421356f);
        Span<float> bands = stackalloc float[BandCount];
        var scale = 4f / FftSize * 2.6f;
        for (var band = 0; band < BandCount; band++)
        {
            var start = _bandStart[band];
            var end = _bandEnd[band];
            float magnitude = 0f;
            for (var bin = start; bin < end; bin++)
            {
                var real = _real[bin];
                var imag = _imag[bin];
                magnitude += MathF.Sqrt((real * real) + (imag * imag));
            }

            var count = Math.Max(end - start, 1);
            magnitude = magnitude / count * scale;
            bands[band] = magnitude < 0f ? 0f : magnitude > 1f ? 1f : magnitude;
        }

        lock (_levels)
        {
            for (var i = 0; i < BandCount; i++)
            {
                _bands[i] = Smooth(_bands[i], bands[i]);
            }

            _rms = Smooth(_rms, rms);
        }
    }

    private bool IsIdle()
    {
        var last = Volatile.Read(ref _lastRead);
        return last != 0 && Stopwatch.GetElapsedTime(last) >= IdleLimit;
    }

    private void ZeroLevels()
    {
        lock (_levels)
        {
            Array.Clear(_bands);
            _rms = 0f;
        }
    }

    private void BuildBands(int sampleRate)
    {
        var binHz = sampleRate / (float)FftSize;
        var maxHz = MathF.Min(14_000f, (sampleRate * 0.5f) - binHz);
        if (maxHz <= 40f)
        {
            maxHz = 40f + binHz;
        }

        var logMin = MathF.Log(40f);
        var logMax = MathF.Log(maxHz);
        for (var band = 0; band < BandCount; band++)
        {
            var startHz = MathF.Exp(logMin + ((logMax - logMin) * band / BandCount));
            var endHz = MathF.Exp(logMin + ((logMax - logMin) * (band + 1) / BandCount));
            var start = (int)MathF.Floor(startHz / binHz);
            var end = (int)MathF.Ceiling(endHz / binHz);
            if (start < 1)
            {
                start = 1;
            }

            if (end <= start)
            {
                end = start + 1;
            }

            if (end > FftSize / 2)
            {
                end = FftSize / 2;
            }

            if (start >= end)
            {
                start = Math.Max(1, end - 1);
            }

            _bandStart[band] = start;
            _bandEnd[band] = end;
        }
    }

    private static void Free(ref IntPtr simple)
    {
        var handle = simple;
        simple = IntPtr.Zero;
        if (handle != IntPtr.Zero)
        {
            FreeSimple(handle);
        }
    }

    private static float Smooth(float current, float target)
    {
        var rate = target > current ? 0.65f : 0.12f;
        var next = current + ((target - current) * rate);
        if (next < 0.0008f)
        {
            return 0f;
        }

        return next > 1f ? 1f : next;
    }

    private static void Transform(float[] real, float[] imag)
    {
        var bitReverse = 0;
        for (var i = 0; i < FftSize; i++)
        {
            if (i < bitReverse)
            {
                (real[i], real[bitReverse]) = (real[bitReverse], real[i]);
                (imag[i], imag[bitReverse]) = (imag[bitReverse], imag[i]);
            }

            var bit = FftSize >> 1;
            while (bit >= 1 && bitReverse >= bit)
            {
                bitReverse -= bit;
                bit >>= 1;
            }

            bitReverse += bit;
        }

        for (var length = 2; length <= FftSize; length <<= 1)
        {
            var angle = -MathF.Tau / length;
            var (stepSin, stepCos) = MathF.SinCos(angle);
            var half = length >> 1;
            for (var start = 0; start < FftSize; start += length)
            {
                float twiddleReal = 1f;
                float twiddleImag = 0f;
                for (var k = 0; k < half; k++)
                {
                    var even = start + k;
                    var odd = even + half;
                    var productReal = (twiddleReal * real[odd]) - (twiddleImag * imag[odd]);
                    var productImag = (twiddleReal * imag[odd]) + (twiddleImag * real[odd]);
                    real[odd] = real[even] - productReal;
                    imag[odd] = imag[even] - productImag;
                    real[even] += productReal;
                    imag[even] += productImag;
                    var nextReal = (twiddleReal * stepCos) - (twiddleImag * stepSin);
                    twiddleImag = (twiddleReal * stepSin) + (twiddleImag * stepCos);
                    twiddleReal = nextReal;
                }
            }
        }
    }

    private static float[] CreateWindow()
    {
        var window = new float[FftSize];
        for (var i = 0; i < FftSize; i++)
        {
            window[i] = 0.5f * (1f - MathF.Cos(MathF.Tau * i / (FftSize - 1)));
        }

        return window;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SampleSpec
    {
        public int Format;
        public uint Rate;
        public byte Channels;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "PulseAudio/PipeWire monitor capture started at {SampleRate} Hz, {Channels} channels")]
    private partial void LogCaptureStarted(int sampleRate, int channels);

    [LoggerMessage(Level = LogLevel.Warning, Message = "PulseAudio/PipeWire monitor capture is unavailable; audio-reactive effects stay at idle")]
    private partial void LogCaptureUnavailable(Exception exception);

    [LibraryImport("libpulse-simple.so.0", EntryPoint = "pa_simple_new", StringMarshalling = StringMarshalling.Utf8)]
    private static unsafe partial IntPtr New(
        string? server,
        string name,
        int direction,
        string? device,
        string streamName,
        ref SampleSpec sampleSpec,
        IntPtr channelMap,
        IntPtr bufferAttributes,
        int* error);

    [LibraryImport("libpulse-simple.so.0", EntryPoint = "pa_simple_read")]
    private static unsafe partial int Read(IntPtr simple, byte* data, nuint bytes, int* error);

    [LibraryImport("libpulse-simple.so.0", EntryPoint = "pa_simple_free")]
    private static partial void FreeSimple(IntPtr simple);
}
