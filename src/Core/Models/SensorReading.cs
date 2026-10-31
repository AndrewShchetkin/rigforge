namespace RigForge.Core.Models;

public enum SensorKind
{
    Temperature,
    Load,
    FanSpeed,
    Duty,
    Power,
    Clock,
    Data,
    Voltage,
}

/// <summary>
/// Semantic role of a sensor. Profiles reference roles ("role:CpuTemperature") instead of
/// hardware-specific ids so that default curves work on any machine and OS.
/// </summary>
public enum SensorRole
{
    None,
    CpuTemperature,
    GpuTemperature,
    LiquidTemperature,
    CpuLoad,
    GpuLoad,
    GpuPower,
    PumpSpeed,
}

/// <param name="Id">Globally unique sensor id, conventionally "{deviceId}/{sensorKey}".</param>
public sealed record SensorReading(string Id, string Name, SensorKind Kind, float Value)
{
    public SensorRole Role { get; init; }

    /// <summary>Time the reading was taken; stamped by the telemetry service.</summary>
    public DateTimeOffset Timestamp { get; init; }

    public string Unit => Kind switch
    {
        SensorKind.Temperature => "°C",
        SensorKind.Load or SensorKind.Duty => "%",
        SensorKind.FanSpeed => "RPM",
        SensorKind.Power => "W",
        SensorKind.Clock => "MHz",
        SensorKind.Data => "MB",
        SensorKind.Voltage => "V",
        _ => string.Empty,
    };
}
