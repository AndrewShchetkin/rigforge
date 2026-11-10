using System.Text.Json;
using RigForge.Core.Models;

namespace RigForge.Application.Effects;

/// <summary>How an effect is grouped in the UI.</summary>
public enum EffectCategory
{
    Flowing,
    Spatial,
    Reactive,
    Static,
    Custom,
}

/// <summary>One choice of a <see cref="EffectParameterKind.Select"/> parameter.</summary>
public sealed record EffectSelectOption(string Value, string Label);

/// <summary>The kind of editor control and validation a parameter needs.</summary>
public enum EffectParameterKind
{
    ColorList,
    FloatRange,
    SensorSelector,
    Boolean,
    IntRange,
    Select,
}

/// <summary>One typed parameter of an effect: the schema the UI form and the validator are built from.</summary>
public sealed record EffectParameterDefinition(
    string Key,
    string Label,
    EffectParameterKind Kind,
    JsonElement DefaultValue,
    double? Min = null,
    double? Max = null,
    double? Step = null,
    string? Unit = null,
    int MinColors = 0,
    int MaxColors = 0,
    IReadOnlyList<EffectSelectOption>? Options = null)
{
    public static EffectParameterDefinition ColorList(string key, string label, IReadOnlyList<string> defaultColors, int minColors, int maxColors) =>
        new(key, label, EffectParameterKind.ColorList, ToElement(defaultColors), MinColors: minColors, MaxColors: maxColors);

    public static EffectParameterDefinition FloatRange(string key, string label, double defaultValue, double min, double max, double step, string? unit = null) =>
        new(key, label, EffectParameterKind.FloatRange, ToElement(defaultValue), Min: min, Max: max, Step: step, Unit: unit);

    public static EffectParameterDefinition SensorSelector(string key, string label, string defaultValue) =>
        new(key, label, EffectParameterKind.SensorSelector, ToElement(defaultValue));

    public static EffectParameterDefinition Boolean(string key, string label, bool defaultValue) =>
        new(key, label, EffectParameterKind.Boolean, ToElement(defaultValue));

    public static EffectParameterDefinition IntRange(string key, string label, int defaultValue, int min, int max, int step = 1, string? unit = null) =>
        new(key, label, EffectParameterKind.IntRange, ToElement(defaultValue), Min: min, Max: max, Step: step, Unit: unit);

    public static EffectParameterDefinition Select(string key, string label, string defaultValue, IReadOnlyList<EffectSelectOption> options) =>
        new(key, label, EffectParameterKind.Select, ToElement(defaultValue), Options: options);

    private static JsonElement ToElement<T>(T value) => JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement.Clone();
}

/// <summary>Static metadata used by the UI to build the parameter form.</summary>
public sealed record EffectDescriptor(
    string Id,
    string Name,
    string Description,
    bool IsAnimated,
    IReadOnlyList<EffectParameterDefinition> Parameters,
    EffectCategory Category = EffectCategory.Flowing,
    bool UsesAudio = false)
{
    /// <summary>True when the effect reacts to a sensor; the render loop wakes it on telemetry updates.</summary>
    public bool UsesSensor => Parameters.Any(p => p.Kind == EffectParameterKind.SensorSelector);

    public EffectParameterDefinition? FindParameter(string key) =>
        Parameters.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal));
}

/// <summary>One real LED position on the virtual canvas, precomputed by <see cref="LedSamplePlan"/>.</summary>
public readonly record struct LedPoint(float X, float Y, bool Enabled);

/// <summary>Per-frame inputs of an effect, with parameter values already parsed from <see cref="EffectSettings.Parameters"/>.</summary>
/// <param name="Time">Seconds since the effect was activated.</param>
/// <param name="Colors">Parsed <c>colors</c> parameter.</param>
/// <param name="Speed">Parsed <c>speed</c> parameter (1 when the effect has none).</param>
/// <param name="Angle">Parsed <c>angle</c> parameter in degrees (0 when the effect has none).</param>
/// <param name="SensorSelector">Parsed <c>sensorSelector</c> parameter (null when the effect has none).</param>
public readonly record struct EffectFrameContext(
    int Width,
    int Height,
    double Time,
    EffectSettings Settings,
    IReadOnlyList<RgbColor> Colors,
    TelemetrySnapshot Telemetry,
    float Speed = 1f,
    float Angle = 0f,
    string? SensorSelector = null,
    float TemperatureLow = 35f,
    float TemperatureHigh = 80f,
    float AudioLevel = 0f,
    ReadOnlyMemory<float> AudioBands = default,
    IReadOnlyDictionary<string, double>? Numbers = null,
    IReadOnlyDictionary<string, bool>? Booleans = null,
    IReadOnlyDictionary<string, string>? Strings = null)
{
    public RgbColor Color(int index) =>
        Colors.Count == 0 ? RgbColor.White : Colors[Math.Clamp(index, 0, Colors.Count - 1)];

    /// <summary>Parsed <see cref="EffectParameterKind.FloatRange"/> value, or <paramref name="fallback"/> when the key was not parsed.</summary>
    public float GetFloat(string key, float fallback = 0f) =>
        Numbers is not null && Numbers.TryGetValue(key, out var value) ? (float)value : fallback;

    /// <summary>Parsed <see cref="EffectParameterKind.IntRange"/> value, or <paramref name="fallback"/> when the key was not parsed.</summary>
    public int GetInt(string key, int fallback = 0) =>
        Numbers is not null && Numbers.TryGetValue(key, out var value) ? (int)value : fallback;

    /// <summary>Parsed <see cref="EffectParameterKind.Boolean"/> value, or <paramref name="fallback"/> when the key was not parsed.</summary>
    public bool GetBool(string key, bool fallback = false) =>
        Booleans is not null && Booleans.TryGetValue(key, out var value) ? value : fallback;

    /// <summary>Parsed <see cref="EffectParameterKind.Select"/> or sensor value, or <paramref name="fallback"/> when the key was not parsed.</summary>
    public string GetString(string key, string fallback = "") =>
        Strings is not null && Strings.TryGetValue(key, out var value) ? value : fallback;

    /// <summary>Normalized band written by <see cref="EffectFrameContext.AudioBands"/>, or 0 when the index is outside the buffer.</summary>
    public float Band(int index)
    {
        var bands = AudioBands.Span;
        return (uint)index < (uint)bands.Length ? bands[index] : 0f;
    }
}

/// <summary>
/// A procedural (or captured) image drawn onto the virtual canvas every frame. Instances are created when the
/// effect becomes active and disposed when it is replaced, so constructors/Dispose may start/stop resources.
/// </summary>
public interface IEffect : IDisposable
{
    EffectDescriptor Descriptor { get; }

    void Render(Span<byte> rgba, in EffectFrameContext context);

    /// <summary>
    /// Direct LED sampling: computes colors only at the real LED points instead of rasterizing the whole
    /// canvas. Returns false when the effect needs the raster canvas (e.g. screen capture); the engine then
    /// falls back to <see cref="Render"/> plus bilinear sampling. Implementations must not allocate per call.
    /// </summary>
    bool TrySamplePoints(ReadOnlySpan<LedPoint> points, Span<RgbColor> destination, in EffectFrameContext context);
}
