namespace RigForge.Core.Models;

/// <summary>Physical arrangement of the LEDs of a zone; used for default layouts and UI rendering.</summary>
public enum LedTopology
{
    Point,
    Linear,
    Ring,
    Composite,

    /// <summary>A row-major 2D grid. Odd rows run right-to-left when the placement is serpentine.</summary>
    Matrix2D,
}
