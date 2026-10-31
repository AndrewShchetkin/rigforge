namespace RigForge.Core.Models;

public enum LcdMode
{
    /// <summary>Gauge for the primary sensor plus two secondary values.</summary>
    Dashboard,

    /// <summary>One large value with an arc gauge.</summary>
    SingleSensor,

    /// <summary>History graph of the primary sensor.</summary>
    Graph,

    /// <summary>Hand the screen back to the device firmware (liquid temperature screen).</summary>
    BuiltInLiquid,
}

public sealed record LcdSettings
{
    public LcdMode Mode { get; init; } = LcdMode.Dashboard;

    public int Brightness { get; init; } = 80;

    /// <summary>Clockwise rotation of the rendered image: 0, 90, 180 or 270.</summary>
    public int Rotation { get; init; }

    /// <summary>Sensors shown on the screen; the first one is the primary value.</summary>
    public IReadOnlyList<string> SensorSelectors { get; init; } =
    [
        TelemetrySnapshot.RoleSelector(SensorRole.CpuTemperature),
        TelemetrySnapshot.RoleSelector(SensorRole.GpuTemperature),
        TelemetrySnapshot.RoleSelector(SensorRole.LiquidTemperature),
    ];

    public string AccentColor { get; init; } = "#8B5CF6";

    public float FramesPerSecond { get; init; } = 2f;
}

/// <summary>Static properties of an AIO screen.</summary>
public sealed record LcdInfo(int Width, int Height, bool IsCircular, float MaxFramesPerSecond);
