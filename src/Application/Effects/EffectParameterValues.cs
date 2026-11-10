using System.Text.Json;
using RigForge.Core.Models;

namespace RigForge.Application.Effects;

/// <summary>Parameter values parsed once per settings change, so the frame loop never touches JSON.</summary>
public readonly record struct ParsedEffectParameters(
    IReadOnlyList<RgbColor> Colors,
    float Speed,
    float Angle,
    string? SensorSelector,
    float TemperatureLow,
    float TemperatureHigh)
{
    public IReadOnlyDictionary<string, double> Numbers { get; init; } = EmptyNumbers;

    public IReadOnlyDictionary<string, bool> Booleans { get; init; } = EmptyBooleans;

    public IReadOnlyDictionary<string, string> Strings { get; init; } = EmptyStrings;

    private static readonly Dictionary<string, double> EmptyNumbers = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, bool> EmptyBooleans = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, string> EmptyStrings = new(StringComparer.Ordinal);
}

/// <summary>Reads typed values out of <see cref="EffectSettings.Parameters"/>, falling back to schema defaults.</summary>
public static class EffectParameterValues
{
    public const string ColorsKey = "colors";
    public const string SpeedKey = "speed";
    public const string AngleKey = "angle";
    public const string SensorSelectorKey = "sensorSelector";
    public const string TemperatureLowKey = "temperatureLow";
    public const string TemperatureHighKey = "temperatureHigh";

    /// <summary>Returns settings whose parameter dictionary contains every key of the effect's schema.</summary>
    public static EffectSettings FillDefaults(EffectSettings settings, EffectDescriptor descriptor)
    {
        var parameters = settings.Parameters;
        if (descriptor.Parameters.All(p => parameters.ContainsKey(p.Key)))
        {
            return settings;
        }

        var filled = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in parameters)
        {
            filled[key] = value;
        }

        foreach (var definition in descriptor.Parameters)
        {
            filled.TryAdd(definition.Key, definition.DefaultValue.Clone());
        }

        return settings with { Parameters = filled };
    }

    public static ParsedEffectParameters Parse(EffectSettings settings, EffectDescriptor descriptor)
    {
        var numbers = new Dictionary<string, double>(StringComparer.Ordinal);
        var booleans = new Dictionary<string, bool>(StringComparer.Ordinal);
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var definition in descriptor.Parameters)
        {
            if (!TryGetValue(settings, definition, out var element))
            {
                continue;
            }

            switch (definition.Kind)
            {
                case EffectParameterKind.FloatRange:
                    if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number))
                    {
                        numbers[definition.Key] = number;
                    }

                    break;
                case EffectParameterKind.IntRange:
                    if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var integer))
                    {
                        numbers[definition.Key] = integer;
                    }

                    break;
                case EffectParameterKind.Boolean:
                    if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        booleans[definition.Key] = element.GetBoolean();
                    }

                    break;
                case EffectParameterKind.Select:
                case EffectParameterKind.SensorSelector:
                    if (element.ValueKind == JsonValueKind.String && element.GetString() is { } text)
                    {
                        strings[definition.Key] = text;
                    }

                    break;
            }
        }

        return new ParsedEffectParameters(
            GetColors(settings, descriptor),
            GetFloat(settings, descriptor, SpeedKey, fallback: 1f),
            GetFloat(settings, descriptor, AngleKey, fallback: 0f),
            GetString(settings, descriptor, SensorSelectorKey),
            GetFloat(settings, descriptor, TemperatureLowKey, fallback: 35f),
            GetFloat(settings, descriptor, TemperatureHighKey, fallback: 80f))
        {
            Numbers = numbers,
            Booleans = booleans,
            Strings = strings,
        };
    }

    /// <summary>The effective raw value of a parameter: the stored value, or the schema default when absent.</summary>
    public static bool TryGetValue(EffectSettings settings, EffectParameterDefinition definition, out JsonElement value)
    {
        if (settings.Parameters is not null && settings.Parameters.TryGetValue(definition.Key, out value))
        {
            return true;
        }

        value = definition.DefaultValue;
        return definition.DefaultValue.ValueKind != JsonValueKind.Undefined;
    }

    public static IReadOnlyList<RgbColor> GetColors(EffectSettings settings, EffectDescriptor descriptor)
    {
        var definition = descriptor.FindParameter(ColorsKey);
        if (definition is null || !TryGetValue(settings, definition, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var colors = new List<RgbColor>();
        foreach (var item in element.EnumerateArray())
        {
            colors.Add(item.ValueKind == JsonValueKind.String && RgbColor.TryParse(item.GetString(), out var color) ? color : RgbColor.White);
        }

        return colors;
    }

    public static float GetFloat(EffectSettings settings, EffectDescriptor descriptor, string key, float fallback)
    {
        var definition = descriptor.FindParameter(key);
        if (definition is not null
            && TryGetValue(settings, definition, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetSingle(out var value))
        {
            return value;
        }

        return fallback;
    }

    public static string? GetString(EffectSettings settings, EffectDescriptor descriptor, string key)
    {
        var definition = descriptor.FindParameter(key);
        if (definition is not null
            && TryGetValue(settings, definition, out var element)
            && element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        return null;
    }
}
