namespace RigForge.Core.Models;

/// <summary>
/// Position of one LED inside its zone, normalized so that (0, 0) is the zone's top-left corner
/// and (1, 1) its bottom-right corner. The effect engine maps it onto the virtual canvas.
/// </summary>
public readonly record struct LedPosition(float X, float Y);
