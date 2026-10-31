using RigForge.Core.Models;

namespace RigForge.Core.Interfaces;

/// <summary>
/// Low-overhead desktop capture for Ambilight. Implementations downscale on the GPU
/// and keep only the latest small frame; capture runs only between <see cref="Start"/> and <see cref="Stop"/>.
/// </summary>
public interface IScreenCapturer : IDisposable
{
    bool IsSupported { get; }

    bool IsRunning { get; }

    void Start();

    void Stop();

    /// <summary>Copies the newest frame if it is newer than <paramref name="destination"/>'s sequence.</summary>
    bool TryCopyLatestFrame(ScreenFrameBuffer destination);
}
