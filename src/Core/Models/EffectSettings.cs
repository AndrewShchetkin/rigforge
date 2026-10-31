namespace RigForge.Core.Models;

/// <summary>Active lighting effect and its parameters. Colors are "#RRGGBB" strings for a stable wire/disk format.</summary>
public sealed record EffectSettings
{
    public string EffectId { get; init; } = "rainbow-wave";

    /// <summary>Global brightness, 0..1.</summary>
    public float Brightness { get; init; } = 1f;

    /// <summary>Animation speed multiplier, 0.05..10.</summary>
    public float Speed { get; init; } = 1f;

    public IReadOnlyList<string> Colors { get; init; } = ["#FF0080", "#00C8FF"];

    /// <summary>Direction of directional effects in degrees (0 = left to right).</summary>
    public float Angle { get; init; }

    /// <summary>Sensor used by reactive effects (e.g. "role:CpuTemperature").</summary>
    public string? SensorSelector { get; init; }

    public float TemperatureLow { get; init; } = 35f;

    public float TemperatureHigh { get; init; } = 80f;

    public int TargetFps { get; init; } = 60;
}
