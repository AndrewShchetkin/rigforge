namespace RigForge.Core.Models;

public enum FanControlMode
{
    Curve,
    Fixed,
}

public sealed record FanCurvePoint(float Temperature, float Duty);

/// <summary>
/// Binds one fan/pump channel to a temperature-driven duty curve.
/// Hysteresis and ramp rates keep the fans from audibly "hunting" around a set point.
/// </summary>
public sealed record FanCurve
{
    public required string Id { get; init; }

    public required string DeviceId { get; init; }

    public required string ChannelId { get; init; }

    public bool Enabled { get; init; } = true;

    public FanControlMode Mode { get; init; } = FanControlMode.Curve;

    public int FixedDuty { get; init; } = 50;

    /// <summary>Temperature sources ("role:CpuTemperature" or sensor ids); the hottest one drives the curve.</summary>
    public IReadOnlyList<string> SensorSelectors { get; init; } = [TelemetrySnapshot.RoleSelector(SensorRole.CpuTemperature)];

    public IReadOnlyList<FanCurvePoint> Points { get; init; } =
    [
        new(30, 25),
        new(50, 40),
        new(65, 60),
        new(75, 80),
        new(85, 100),
    ];

    /// <summary>The temperature must fall this far below the value that set the current duty before the duty is lowered.</summary>
    public float HysteresisCelsius { get; init; } = 2f;

    /// <summary>Maximum duty increase per second (0 = immediate).</summary>
    public float RampUpPercentPerSecond { get; init; } = 10f;

    /// <summary>Maximum duty decrease per second (0 = immediate).</summary>
    public float RampDownPercentPerSecond { get; init; } = 3f;

    public int MinDuty { get; init; }

    public int MaxDuty { get; init; } = 100;

    /// <summary>Time constant of the exponential temperature smoothing (0 = disabled).</summary>
    public float TemperatureSmoothingSeconds { get; init; }
}
