using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using SharpGen.Runtime;

namespace RigForge.Infrastructure.Windows.Capture;

/// <summary>
/// Ambilight source based on DXGI Desktop Duplication. Runs on its own thread only while an effect needs it and
/// recreates the session after mode changes, UAC prompts or exclusive fullscreen switches (DXGI_ERROR_ACCESS_LOST).
/// Note: duplication only works inside an interactive user session, not from a session-0 Windows service.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class DxgiScreenCapturer(IOptions<WindowsPlatformOptions> options, ILogger<DxgiScreenCapturer> logger) : IScreenCapturer
{
    private static readonly TimeSpan RecoveryDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan FailureDelay = TimeSpan.FromSeconds(5);

    private readonly Lock _frameLock = new();
    private readonly Lock _lifecycle = new();
    private byte[] _latest = [];
    private int _width;
    private int _height;
    private long _sequence;
    private Thread? _thread;
    private CancellationTokenSource? _stop;
    private bool _failureLogged;

    public bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(6, 2);

    public bool IsRunning
    {
        get
        {
            lock (_lifecycle)
            {
                return _thread is not null;
            }
        }
    }

    public void Start()
    {
        lock (_lifecycle)
        {
            if (_thread is not null)
            {
                return;
            }

            _stop = new CancellationTokenSource();
            var token = _stop.Token;
            _thread = new Thread(() => CaptureLoop(token)) { IsBackground = true, Name = "RigForge DXGI capture" };
            _thread.Start();
        }
    }

    public void Stop()
    {
        Thread? thread;
        CancellationTokenSource? stop;
        lock (_lifecycle)
        {
            (thread, stop) = (_thread, _stop);
            (_thread, _stop) = (null, null);
        }

        if (thread is null || stop is null)
        {
            return;
        }

        stop.Cancel();
        thread.Join(TimeSpan.FromSeconds(2));
        stop.Dispose();
    }

    public bool TryCopyLatestFrame(ScreenFrameBuffer destination)
    {
        lock (_frameLock)
        {
            if (_sequence == 0 || _sequence == destination.Sequence)
            {
                return false;
            }

            destination.CopyFrom(_width, _height, _sequence, _latest);
            return true;
        }
    }

    public void Dispose() => Stop();

    private void CaptureLoop(CancellationToken token)
    {
        var settings = options.Value.Capture;
        var frameInterval = TimeSpan.FromSeconds(1.0 / Math.Clamp(settings.MaxFramesPerSecond, 1, 120));

        while (!token.IsCancellationRequested)
        {
            try
            {
                using var session = new DuplicationSession(settings.OutputIndex, Math.Max(16, settings.TargetWidth));
                LogSessionStarted(settings.OutputIndex, session.SourceWidth, session.SourceHeight, session.Width, session.Height);
                _failureLogged = false;

                var clock = Stopwatch.StartNew();
                while (!token.IsCancellationRequested)
                {
                    var started = clock.Elapsed;
                    if (session.TryCapture(100) is { } pixels)
                    {
                        Publish(session.Width, session.Height, pixels);
                    }

                    var remaining = frameInterval - (clock.Elapsed - started);
                    if (remaining > TimeSpan.Zero)
                    {
                        token.WaitHandle.WaitOne(remaining);
                    }
                }
            }
            catch (SharpGenException ex) when (ex.ResultCode == Vortice.DXGI.ResultCode.AccessLost)
            {
                token.WaitHandle.WaitOne(RecoveryDelay);
            }
            catch (Exception ex)
            {
                if (!_failureLogged)
                {
                    LogCaptureFailed(ex);
                    _failureLogged = true;
                }

                token.WaitHandle.WaitOne(FailureDelay);
            }
        }
    }

    private void Publish(int width, int height, byte[] pixels)
    {
        lock (_frameLock)
        {
            if (_latest.Length != pixels.Length)
            {
                _latest = new byte[pixels.Length];
            }

            pixels.CopyTo(_latest, 0);
            _width = width;
            _height = height;
            _sequence++;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Screen capture of monitor #{Output} started: {SourceWidth}x{SourceHeight} downscaled to {Width}x{Height}")]
    private partial void LogSessionStarted(int output, int sourceWidth, int sourceHeight, int width, int height);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Screen capture is unavailable (Desktop Duplication needs an interactive user session); retrying every 5 s")]
    private partial void LogCaptureFailed(Exception exception);
}
