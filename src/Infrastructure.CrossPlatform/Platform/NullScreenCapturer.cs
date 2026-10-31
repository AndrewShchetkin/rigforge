using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.Platform;

/// <summary>Used on platforms without a capture backend; the Ambilight effect then renders black.</summary>
internal sealed class NullScreenCapturer : IScreenCapturer
{
    public bool IsSupported => false;

    public bool IsRunning => false;

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public bool TryCopyLatestFrame(ScreenFrameBuffer destination) => false;

    public void Dispose()
    {
    }
}
