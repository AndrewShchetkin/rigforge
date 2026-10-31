using RigForge.Core.Models;

namespace RigForge.Core.Interfaces;

/// <summary>Screen embedded in an AIO pump head.</summary>
public interface IAioLcdDevice : IHardwareDevice
{
    LcdInfo Lcd { get; }

    /// <summary>Displays a frame given as tightly packed RGBA8888 of <see cref="LcdInfo.Width"/> x <see cref="LcdInfo.Height"/> pixels.</summary>
    ValueTask PushFrameAsync(ReadOnlyMemory<byte> rgba, CancellationToken cancellationToken);

    ValueTask SetBrightnessAsync(int percent, CancellationToken cancellationToken);

    /// <summary>Switches back to the firmware's own screen (e.g. liquid temperature).</summary>
    ValueTask ShowBuiltInScreenAsync(CancellationToken cancellationToken);
}
