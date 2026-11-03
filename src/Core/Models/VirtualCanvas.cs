namespace RigForge.Core.Models;

/// <summary>
/// The shared 2D space (in abstract "canvas pixels") that every RGB zone is placed on.
/// Effects are rendered once per frame at exactly this resolution and sampled per LED.
/// </summary>
public sealed record VirtualCanvas
{
    public int Width { get; init; } = 320;

    public int Height { get; init; } = 180;

    public IReadOnlyList<ZonePlacement> Placements { get; init; } = [];
}

/// <summary>
/// Position of one zone on the canvas. (X, Y) is the top-left corner of the unrotated box;
/// rotation is applied around that corner (same convention as Konva nodes in the UI).
/// </summary>
public sealed record ZonePlacement
{
    public required string DeviceId { get; init; }

    public required string ZoneId { get; init; }

    public float X { get; init; }

    public float Y { get; init; }

    public float Width { get; init; } = 40f;

    public float Height { get; init; } = 40f;

    /// <summary>Clockwise rotation in degrees.</summary>
    public float Rotation { get; init; }

    public bool Enabled { get; init; } = true;

    /// <summary>LED count override for zones whose size the hardware cannot detect (ARGB headers).</summary>
    public int? LedCount { get; init; }

    /// <summary>
    /// Distinguishes one part of a physical zone (an ARGB chain) from its siblings.
    /// Null when the zone is a single placement.
    /// </summary>
    public string? SegmentId { get; init; }

    /// <summary>User-facing name of a segment, for example "Fan 1" or "CPU block".</summary>
    public string? Name { get; init; }

    /// <summary>Layout override for this placement. Null keeps the hardware zone's own topology.</summary>
    public LedTopology? Topology { get; init; }

    /// <summary>Column count used when <see cref="Topology"/> is <see cref="LedTopology.Matrix2D"/>. Defaults to 8.</summary>
    public int? MatrixColumns { get; init; }

    /// <summary>Reverses odd matrix rows so a zigzag strip matches the grid.</summary>
    public bool Serpentine { get; init; }
}
