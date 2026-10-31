namespace RigForge.Application;

public sealed class RenderingOptions
{
    public const string SectionName = "Rendering";

    /// <summary>Hard cap for the lighting loop regardless of the profile's TargetFps.</summary>
    public int MaxFramesPerSecond { get; set; } = 60;

    /// <summary>Loop rate for non-animated effects; keeps reactive effects and settings changes responsive.</summary>
    public int StaticEffectFramesPerSecond { get; set; } = 10;

    public int PreviewFramesPerSecond { get; set; } = 20;

    /// <summary>Unchanged frames are re-sent at this interval in case a controller reset itself.</summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(2);
}

public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan ProviderTimeout { get; set; } = TimeSpan.FromMilliseconds(800);

    /// <summary>Readings that were not refreshed for this long are dropped from the snapshot.</summary>
    public TimeSpan ReadingTimeToLive { get; set; } = TimeSpan.FromSeconds(15);
}

public sealed class CoolingOptions
{
    public const string SectionName = "Cooling";

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Duty applied when a curve loses all of its temperature sources or the coolant is critical.</summary>
    public int FailsafeDutyPercent { get; set; } = 100;

    public TimeSpan SensorStaleAfter { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Liquid temperature at which every controlled channel is forced to the failsafe duty.</summary>
    public float CriticalLiquidTemperature { get; set; } = 60f;

    /// <summary>Pumps are never driven below this duty, whatever the curve says.</summary>
    public int MinimumPumpDutyPercent { get; set; } = 30;

    /// <summary>Unchanged duties are re-sent at this interval in case a controller reset itself.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);
}

public sealed class LcdOptions
{
    public const string SectionName = "Lcd";

    public float MaxFramesPerSecond { get; set; } = 10f;

    /// <summary>Samples kept for the graph mode (one per telemetry poll).</summary>
    public int HistorySamples { get; set; } = 120;

    /// <summary>An unchanged frame is re-sent at this interval.</summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(10);
}
