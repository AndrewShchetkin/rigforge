using Microsoft.Extensions.Logging;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Infrastructure.Linux.Capture;

/// <summary>
/// Placeholder for Ambilight on Linux. The intended backend is the xdg-desktop-portal ScreenCast interface
/// (works on Wayland and X11): request a PipeWire stream over D-Bus, consume SPA video buffers (ideally DMA-BUF
/// downscaled on the GPU) and publish small BGRA frames exactly like the Windows DXGI capturer does.
/// </summary>
internal sealed partial class LinuxScreenCapturerStub(ILogger<LinuxScreenCapturerStub> logger) : IScreenCapturer
{
    private bool _warned;

    public bool IsSupported => false;

    public bool IsRunning => false;

    public void Start()
    {
        if (!_warned)
        {
            LogNotImplemented();
            _warned = true;
        }
    }

    public void Stop()
    {
    }

    public bool TryCopyLatestFrame(ScreenFrameBuffer destination) => false;

    public void Dispose()
    {
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Screen capture (PipeWire / xdg-desktop-portal) is not implemented yet on Linux; Ambilight renders black")]
    private partial void LogNotImplemented();
}
