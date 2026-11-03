namespace RigForge.Core.Models;

/// <summary>Everything the user configures; persisted as a single JSON document.</summary>
public sealed record RigProfile
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public VirtualCanvas Canvas { get; init; } = new();

    public EffectSettings Effect { get; init; } = new();

    public IReadOnlyList<FanCurve> FanCurves { get; init; } = [];

    public LcdSettings Lcd { get; init; } = new();

    /// <summary>Optional gamma and white balance, one entry per device.</summary>
    public IReadOnlyList<DeviceColorCorrection> ColorCorrections { get; init; } = [];
}
