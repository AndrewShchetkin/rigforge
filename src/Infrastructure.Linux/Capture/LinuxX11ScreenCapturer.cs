using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Infrastructure.Linux.Capture;

/// <summary>
/// Ambilight capture through X11 / XWayland. <c>libX11.so.6</c> is loaded dynamically, so a headless
/// Wayland session without <c>DISPLAY</c> reports <see cref="IsSupported"/> false and never throws.
/// Frames are downscaled to 160×90 BGRA. The native <c>XImage</c> is destroyed before the next grab.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class LinuxX11ScreenCapturer(ILogger<LinuxX11ScreenCapturer> logger) : IScreenCapturer
{
    private const int TargetWidth = 160;
    private const int TargetHeight = 90;
    private const int ZPixmap = 2;
    private const int DestroyImageOffset = 96;

    private static readonly IntPtr NativeHandle = LoadLibrary();

    private readonly Lock _frameLock = new();
    private readonly Lock _lifecycle = new();
    private readonly byte[] _frame = new byte[TargetWidth * TargetHeight * 4];
    private byte[] _latest = [];
    private int _width;
    private int _height;
    private long _sequence;
    private Thread? _thread;
    private CancellationTokenSource? _stop;
    private bool _failureLogged;

    public bool IsSupported =>
        NativeHandle != IntPtr.Zero
        && IntPtr.Size == 8
        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));

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
        if (!IsSupported)
        {
            return;
        }

        lock (_lifecycle)
        {
            if (_thread is not null)
            {
                return;
            }

            _stop = new CancellationTokenSource();
            var token = _stop.Token;
            _thread = new Thread(() => CaptureLoop(token))
            {
                IsBackground = true,
                Name = "RigForge X11 capture",
            };
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
        var display = IntPtr.Zero;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (display == IntPtr.Zero)
                {
                    display = XOpenDisplay(IntPtr.Zero);
                    if (display == IntPtr.Zero)
                    {
                        token.WaitHandle.WaitOne(TimeSpan.FromSeconds(1));
                        continue;
                    }
                }

                try
                {
                    CaptureRoot(display);
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    if (!_failureLogged)
                    {
                        _failureLogged = true;
                        LogCaptureFailed(ex);
                    }
                }

                token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(33));
            }
        }
        finally
        {
            if (display != IntPtr.Zero)
            {
                XCloseDisplay(display);
            }
        }
    }

    private unsafe void CaptureRoot(IntPtr display)
    {
        var screen = XDefaultScreen(display);
        var root = XRootWindow(display, screen);
        Span<byte> attributes = stackalloc byte[256];
        fixed (byte* attributePointer = attributes)
        {
            if (XGetWindowAttributes(display, root, attributePointer) == 0)
            {
                return;
            }
        }

        var sourceWidth = MemoryMarshal.Read<int>(attributes[8..]);
        var sourceHeight = MemoryMarshal.Read<int>(attributes[12..]);
        if (sourceWidth <= 0 || sourceHeight <= 0 || sourceWidth > 16384 || sourceHeight > 16384)
        {
            return;
        }

        var image = XGetImage(display, root, 0, 0, (uint)sourceWidth, (uint)sourceHeight, unchecked((nuint)(-1)), ZPixmap);
        if (image == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var imageWidth = Marshal.ReadInt32(image, 0);
            var imageHeight = Marshal.ReadInt32(image, 4);
            var data = Marshal.ReadIntPtr(image, 16);
            var stride = Marshal.ReadInt32(image, 44);
            var bitsPerPixel = Marshal.ReadInt32(image, 48);
            if (imageWidth > 0 && imageHeight > 0 && data != IntPtr.Zero && stride > 0 && bitsPerPixel >= 24)
            {
                Downscale((byte*)data, imageWidth, imageHeight, stride, bitsPerPixel / 8);
                Publish();
            }
        }
        finally
        {
            DestroyImage(image);
        }
    }

    private unsafe void Downscale(byte* data, int sourceWidth, int sourceHeight, int stride, int bytesPerPixel)
    {
        for (var y = 0; y < TargetHeight; y++)
        {
            var sourceY = (int)((long)y * sourceHeight / TargetHeight);
            if (sourceY >= sourceHeight)
            {
                sourceY = sourceHeight - 1;
            }

            for (var x = 0; x < TargetWidth; x++)
            {
                var sourceX = (int)((long)x * sourceWidth / TargetWidth);
                if (sourceX >= sourceWidth)
                {
                    sourceX = sourceWidth - 1;
                }

                var source = data + (sourceY * stride) + (sourceX * bytesPerPixel);
                var destination = ((y * TargetWidth) + x) * 4;
                _frame[destination] = source[0];
                _frame[destination + 1] = bytesPerPixel > 1 ? source[1] : (byte)0;
                _frame[destination + 2] = bytesPerPixel > 2 ? source[2] : (byte)0;
                _frame[destination + 3] = 255;
            }
        }
    }

    private void Publish()
    {
        lock (_frameLock)
        {
            if (_latest.Length != _frame.Length)
            {
                _latest = new byte[_frame.Length];
            }

            _frame.AsSpan().CopyTo(_latest);
            _width = TargetWidth;
            _height = TargetHeight;
            _sequence++;
        }
    }

    private static unsafe void DestroyImage(IntPtr image)
    {
        var destroy = Marshal.ReadIntPtr(image, DestroyImageOffset);
        if (destroy == IntPtr.Zero)
        {
            return;
        }

        ((delegate* unmanaged[Cdecl]<IntPtr, int>)destroy)(image);
    }

    private static IntPtr LoadLibrary()
    {
        if (!OperatingSystem.IsLinux())
        {
            return IntPtr.Zero;
        }

        return NativeLibrary.TryLoad("libX11.so.6", out var handle) ? handle : IntPtr.Zero;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "X11 screen capture failed; Ambilight retries")]
    private partial void LogCaptureFailed(Exception exception);

    [LibraryImport("libX11.so.6")]
    private static partial IntPtr XOpenDisplay(IntPtr displayName);

    [LibraryImport("libX11.so.6")]
    private static partial int XCloseDisplay(IntPtr display);

    [LibraryImport("libX11.so.6")]
    private static partial int XDefaultScreen(IntPtr display);

    [LibraryImport("libX11.so.6")]
    private static partial nuint XRootWindow(IntPtr display, int screen);

    [LibraryImport("libX11.so.6")]
    private static unsafe partial int XGetWindowAttributes(IntPtr display, nuint window, byte* attributes);

    [LibraryImport("libX11.so.6")]
    private static partial IntPtr XGetImage(IntPtr display, nuint drawable, int x, int y, uint width, uint height, nuint planeMask, int format);
}
