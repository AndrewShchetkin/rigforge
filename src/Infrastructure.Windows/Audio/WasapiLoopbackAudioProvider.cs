using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using RigForge.Core.Interfaces;

namespace RigForge.Infrastructure.Windows.Audio;

/// <summary>
/// System-audio levels from the default render endpoint in loopback mode.
/// Capture starts on the first <see cref="ReadSpectrum"/> call and the device is released after
/// five seconds without a reader, so an idle rig holds no audio handle and uses no CPU.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WasapiLoopbackAudioProvider(ILogger<WasapiLoopbackAudioProvider> logger) : IAudioLevelProvider
{
    private const int FftSize = 256;
    private const int Hop = 256;
    private const int BandCount = 16;
    private const int ClsCtxAll = 23;
    private const int ShareModeShared = 0;
    private const int RenderFlow = 0;
    private const int ConsoleRole = 0;
    private const uint LoopbackFlag = 0x00020000;
    private const int SilentBuffer = 0x2;
    private const uint BufferNotAligned = 0x88890019;
    private const ushort WaveFormatPcm = 1;
    private const ushort WaveFormatIeeeFloat = 3;
    private const ushort WaveFormatExtensible = 0xFFFE;

    private static readonly Guid EnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid AudioClientId = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    private static readonly Guid CaptureClientId = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
    private static readonly Guid IeeeFloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");
    private static readonly Guid PcmSubtype = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly TimeSpan IdleLimit = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

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

    private IMMDeviceEnumerator? _enumerator;
    private IMMDevice? _device;
    private IAudioClient? _client;
    private IAudioCaptureClient? _capture;
    private Thread? _thread;
    private long _lastRead;
    private float _rms;
    private int _channels;
    private int _sampleRate;
    private int _bytesPerFrame;
    private int _ringPos;
    private int _filled;
    private int _sinceHop;
    private bool _isFloat;
    private bool _started;
    private bool _loggedFailure;
    private bool _loggedStart;
    private volatile bool _running;
    private volatile bool _disposed;

    public bool IsSupported => OperatingSystem.IsWindows();

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
            // The capture thread still owns the COM objects and the cancellation source.
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

            Volatile.Write(ref _lastRead, Stopwatch.GetTimestamp());
            _running = true;
            _thread = new Thread(CaptureLoop)
            {
                IsBackground = true,
                Name = "RigForge WASAPI loopback",
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

    private void CaptureLoop()
    {
        try
        {
            var token = _stop.Token;
            while (!token.IsCancellationRequested && !_disposed && !IsIdle())
            {
                try
                {
                    if (_client is null)
                    {
                        OpenCapture();
                        if (!_loggedStart)
                        {
                            _loggedStart = true;
                            LogCaptureStarted(_sampleRate, _channels);
                        }
                    }

                    if (!Drain())
                    {
                        Decay();
                    }
                }
                catch (Exception ex) when (!token.IsCancellationRequested && !_disposed)
                {
                    ReleaseCapture();
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

                    continue;
                }

                if (token.WaitHandle.WaitOne(PollInterval))
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (!_disposed)
        {
            // A fault on this thread must not tear down the process.
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                LogCaptureUnavailable(ex);
            }

            ZeroLevels();
        }
        finally
        {
            ReleaseCapture();
            ZeroLevels();
            lock (_lifecycle)
            {
                _thread = null;
                _running = false;
            }
        }
    }

    private bool IsIdle()
    {
        var last = Volatile.Read(ref _lastRead);
        return last != 0 && Stopwatch.GetElapsedTime(last) >= IdleLimit;
    }

    private void OpenCapture()
    {
        var enumeratorType = Type.GetTypeFromCLSID(EnumeratorClsid, throwOnError: true)
            ?? throw new InvalidOperationException("MMDeviceEnumerator is not registered.");
        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
        _enumerator = enumerator;
        enumerator.GetDefaultAudioEndpoint(RenderFlow, ConsoleRole, out var device);
        _device = device;

        var client = ActivateClient(device);
        _client = client;
        var formatPtr = IntPtr.Zero;
        try
        {
            client.GetMixFormat(out formatPtr);
            if (formatPtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("WASAPI returned an empty mix format.");
            }

            ReadFormat(formatPtr);
            try
            {
                client.Initialize(ShareModeShared, LoopbackFlag, 0, 0, formatPtr, IntPtr.Zero);
            }
            catch (COMException ex) when ((uint)ex.HResult == BufferNotAligned)
            {
                client.GetBufferSize(out var frames);
                var duration = (long)Math.Round(10_000_000d * frames / Math.Max(_sampleRate, 1));
                Marshal.ReleaseComObject(client);
                _client = null;
                client = ActivateClient(device);
                _client = client;
                client.Initialize(ShareModeShared, LoopbackFlag, duration, 0, formatPtr, IntPtr.Zero);
            }

            var captureId = CaptureClientId;
            client.GetService(ref captureId, out var service);
            _capture = Unwrap<IAudioCaptureClient>(service);
            client.Start();
            _started = true;
        }
        finally
        {
            if (formatPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(formatPtr);
            }
        }
    }

    private static IAudioClient ActivateClient(IMMDevice device)
    {
        var iid = AudioClientId;
        device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var unknown);
        return Unwrap<IAudioClient>(unknown);
    }

    private static T Unwrap<T>(object instance) where T : class
    {
        if (instance is T typed)
        {
            if (!ReferenceEquals(instance, typed))
            {
                Marshal.ReleaseComObject(instance);
            }

            return typed;
        }

        Marshal.ReleaseComObject(instance);
        throw new InvalidCastException($"WASAPI returned {instance.GetType().Name} instead of {typeof(T).Name}.");
    }

    private void ReadFormat(IntPtr format)
    {
        var tag = (ushort)Marshal.ReadInt16(format, 0);
        var channels = (ushort)Marshal.ReadInt16(format, 2);
        var sampleRate = Marshal.ReadInt32(format, 4);
        var blockAlign = (ushort)Marshal.ReadInt16(format, 12);
        var bits = (ushort)Marshal.ReadInt16(format, 14);
        var extra = (ushort)Marshal.ReadInt16(format, 16);
        var isFloat = tag == WaveFormatIeeeFloat;
        var isPcm = tag == WaveFormatPcm;
        if (tag == WaveFormatExtensible)
        {
            if (extra < 22)
            {
                throw new InvalidOperationException("WASAPI extensible format is truncated.");
            }

            var subType = Marshal.PtrToStructure<Guid>(IntPtr.Add(format, 24));
            isFloat = subType == IeeeFloatSubtype;
            isPcm = subType == PcmSubtype;
        }

        if (channels is < 1 or > 32 || sampleRate is < 8_000 or > 384_000 || blockAlign < 1)
        {
            throw new InvalidOperationException("WASAPI mix format is not usable.");
        }

        var sampleBytes = isFloat ? 4 : 2;
        if (isFloat)
        {
            if (bits != 32 || blockAlign < channels * sampleBytes)
            {
                throw new InvalidOperationException("WASAPI float mix format is not 32-bit.");
            }
        }
        else if (!isPcm || bits != 16 || blockAlign < channels * sampleBytes)
        {
            throw new InvalidOperationException("WASAPI mix format is neither IEEE float nor 16-bit PCM.");
        }

        _channels = channels;
        _sampleRate = sampleRate;
        _bytesPerFrame = blockAlign;
        _isFloat = isFloat;
        _filled = 0;
        _ringPos = 0;
        _sinceHop = 0;
        BuildBands(sampleRate);
    }

    private void BuildBands(int sampleRate)
    {
        var binHz = sampleRate / (float)FftSize;
        var maxHz = MathF.Min(14_000f, sampleRate * 0.5f - binHz);
        if (maxHz <= 40f)
        {
            maxHz = 40f + binHz;
        }

        var logMin = MathF.Log(40f);
        var logMax = MathF.Log(maxHz);
        for (var band = 0; band < BandCount; band++)
        {
            var startHz = MathF.Exp(logMin + (logMax - logMin) * band / BandCount);
            var endHz = MathF.Exp(logMin + (logMax - logMin) * (band + 1) / BandCount);
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

    private bool Drain()
    {
        var capture = _capture;
        if (capture is null)
        {
            return false;
        }

        var read = false;
        for (var packetIndex = 0; packetIndex < 32; packetIndex++)
        {
            capture.GetNextPacketSize(out var packet);
            if (packet <= 0)
            {
                return read;
            }

            capture.GetBuffer(out var data, out var frames, out var flags, out _, out _);
            try
            {
                if (frames <= 0)
                {
                    continue;
                }

                read = true;
                if ((flags & SilentBuffer) != 0 || data == IntPtr.Zero)
                {
                    var count = Math.Min(frames, FftSize);
                    for (var i = 0; i < count; i++)
                    {
                        Push(0f);
                    }
                }
                else
                {
                    Consume(data, frames);
                }
            }
            finally
            {
                capture.ReleaseBuffer(frames);
            }
        }

        return read;
    }

    private unsafe void Consume(IntPtr data, int frames)
    {
        var channels = _channels;
        var block = _bytesPerFrame;
        var sampleBytes = _isFloat ? 4 : 2;
        if (data == IntPtr.Zero || frames <= 0 || channels <= 0 || block < channels * sampleBytes)
        {
            return;
        }

        var take = Math.Min(frames, 4096);
        var bytes = (byte*)data + (long)(frames - take) * block;
        for (var i = 0; i < take; i++)
        {
            var frame = bytes + (long)i * block;
            float sum = 0f;
            if (_isFloat)
            {
                var samples = (float*)frame;
                for (var channel = 0; channel < channels; channel++)
                {
                    var sample = samples[channel];
                    if (float.IsFinite(sample))
                    {
                        sum += sample;
                    }
                }
            }
            else
            {
                var samples = (short*)frame;
                for (var channel = 0; channel < channels; channel++)
                {
                    sum += samples[channel];
                }

                sum *= 1f / 32768f;
            }

            Push(sum / channels);
        }
    }

    private void Push(float sample)
    {
        if (!float.IsFinite(sample))
        {
            sample = 0f;
        }
        else if (sample > 1f)
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
                var re = _real[bin];
                var im = _imag[bin];
                magnitude += MathF.Sqrt(re * re + im * im);
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

    private void Decay()
    {
        lock (_levels)
        {
            for (var i = 0; i < BandCount; i++)
            {
                _bands[i] *= 0.9f;
                if (_bands[i] < 0.0008f)
                {
                    _bands[i] = 0f;
                }
            }

            _rms *= 0.9f;
            if (_rms < 0.0008f)
            {
                _rms = 0f;
            }
        }
    }

    private void ZeroLevels()
    {
        lock (_levels)
        {
            Array.Clear(_bands);
            _rms = 0f;
        }
    }

    private void ReleaseCapture()
    {
        if (_started && _client is not null)
        {
            try
            {
                _client.Stop();
            }
            catch (Exception)
            {
                // The endpoint may already be gone.
            }
        }

        _started = false;
        Release(ref _capture);
        Release(ref _client);
        Release(ref _device);
        Release(ref _enumerator);
        _filled = 0;
        _ringPos = 0;
        _sinceHop = 0;
    }

    private static void Release<T>(ref T? com) where T : class
    {
        var instance = com;
        com = null;
        if (instance is null || !Marshal.IsComObject(instance))
        {
            return;
        }

        try
        {
            Marshal.ReleaseComObject(instance);
        }
        catch (Exception)
        {
            // Already released.
        }
    }

    private static float Smooth(float current, float target)
    {
        var rate = target > current ? 0.65f : 0.12f;
        var next = current + (target - current) * rate;
        if (next < 0.0008f)
        {
            return 0f;
        }

        return next > 1f ? 1f : next;
    }

    private static void Transform(float[] real, float[] imag)
    {
        var j = 0;
        for (var i = 0; i < FftSize; i++)
        {
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }

            var bit = FftSize >> 1;
            while (bit >= 1 && j >= bit)
            {
                j -= bit;
                bit >>= 1;
            }

            j += bit;
        }

        for (var length = 2; length <= FftSize; length <<= 1)
        {
            var angle = -MathF.Tau / length;
            var (stepSin, stepCos) = MathF.SinCos(angle);
            var half = length >> 1;
            for (var start = 0; start < FftSize; start += length)
            {
                float wRe = 1f;
                float wIm = 0f;
                for (var k = 0; k < half; k++)
                {
                    var even = start + k;
                    var odd = even + half;
                    var tRe = wRe * real[odd] - wIm * imag[odd];
                    var tIm = wRe * imag[odd] + wIm * real[odd];
                    real[odd] = real[even] - tRe;
                    imag[odd] = imag[even] - tIm;
                    real[even] += tRe;
                    imag[even] += tIm;
                    var nextRe = wRe * stepCos - wIm * stepSin;
                    wIm = wRe * stepSin + wIm * stepCos;
                    wRe = nextRe;
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

    [LoggerMessage(Level = LogLevel.Information, Message = "WASAPI loopback capture started at {SampleRate} Hz, {Channels} channels")]
    private partial void LogCaptureStarted(int sampleRate, int channels);

    [LoggerMessage(Level = LogLevel.Warning, Message = "WASAPI loopback capture is unavailable; audio-reactive effects stay at idle until a playback device is present")]
    private partial void LogCaptureUnavailable(Exception exception);
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

    void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    void Activate(ref Guid interfaceId, uint classContext, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
}

[ComImport]
[Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    void Initialize(int shareMode, uint streamFlags, long bufferDurationHns, long periodicityHns, IntPtr format, IntPtr session);

    void GetBufferSize(out int bufferFrames);

    void GetStreamLatency(out long latencyHns);

    void GetCurrentPadding(out int paddingFrames);

    void IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);

    void GetMixFormat(out IntPtr deviceFormat);

    void GetDevicePeriod(out long defaultPeriodHns, out long minimumPeriodHns);

    void Start();

    void Stop();

    void Reset();

    void SetEventHandle(IntPtr handle);

    void GetService(ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
}

[ComImport]
[Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    void GetBuffer(out IntPtr data, out int frames, out int flags, out long devicePosition, out long qpcPosition);

    void ReleaseBuffer(int framesRead);

    void GetNextPacketSize(out int frames);
}
