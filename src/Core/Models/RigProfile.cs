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

    /// <summary>
    /// Per-device LCD settings keyed by device id. <see cref="Lcd"/> remains the fallback for devices without an entry.
    /// </summary>
    public IReadOnlyDictionary<string, LcdSettings> Displays { get; init; } =
        new Dictionary<string, LcdSettings>(StringComparer.Ordinal);

    /// <summary>Optional gamma and white balance, one entry per device.</summary>
    public IReadOnlyList<DeviceColorCorrection> ColorCorrections { get; init; } = [];

    /// <summary>Named color lists the user saved. Built-in palettes are not stored here.</summary>
    public IReadOnlyList<ColorPalette> ColorPalettes { get; init; } = [];
}
