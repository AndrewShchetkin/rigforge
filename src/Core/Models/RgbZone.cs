namespace RigForge.Core.Models;

/// <summary>
/// A group of LEDs that is placed on the virtual canvas as one unit
/// (a motherboard zone, an ARGB header, an NZXT channel with its daisy-chained accessories...).
/// </summary>
public sealed record RgbZone
{
    /// <summary>Identifier, unique within the owning device (e.g. "argb1", "mainboard", "led2").</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required LedTopology Topology { get; init; }

    /// <summary>LED positions in device order, normalized to the zone's bounding box.</summary>
    public required IReadOnlyList<LedPosition> Leds { get; init; }

    public int LedCount => Leds.Count;

    /// <summary>True when the hardware cannot report the LED count and the user must configure it (e.g. ASUS ARGB headers).</summary>
    public bool IsResizable { get; init; }

    public int MaxLedCount { get; init; }
}
