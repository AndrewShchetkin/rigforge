using RigForge.Core.Models;

namespace RigForge.Core.Interfaces;

/// <summary>An addressable lighting device driven in "direct" (per-frame streaming) mode.</summary>
public interface IRgbDevice : IHardwareDevice
{
    /// <summary>Current zones; the reference changes when a zone is resized.</summary>
    IReadOnlyList<RgbZone> Zones { get; }

    /// <summary>Upper bound of frames per second the controller handles reliably.</summary>
    int MaxFramesPerSecond { get; }

    /// <summary>Changes the LED count of a zone with <see cref="RgbZone.IsResizable"/> set.</summary>
    bool TryResizeZone(string zoneId, int ledCount);

    /// <summary>Sends one frame: LEDs of all zones concatenated in <see cref="Zones"/> order.</summary>
    ValueTask UpdateLedsAsync(ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken);
}
