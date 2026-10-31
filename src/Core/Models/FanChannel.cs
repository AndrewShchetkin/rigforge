namespace RigForge.Core.Models;

public enum FanChannelKind
{
    Fan,
    Pump,
}

/// <summary>A PWM/DC output of a fan controller.</summary>
/// <param name="Id">Identifier, unique within the owning device (e.g. "pump", "fan1", "pwm2").</param>
/// <param name="MinDutyPercent">Lowest duty the hardware accepts (pumps usually refuse to go below ~20 %).</param>
/// <param name="SpeedSensorId">Sensor reporting this channel's RPM, if the hardware exposes one.</param>
public sealed record FanChannel(
    string Id,
    string Name,
    FanChannelKind Kind,
    int MinDutyPercent,
    int MaxDutyPercent,
    string? SpeedSensorId = null);
