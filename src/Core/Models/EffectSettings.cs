using System.Text.Json;
using System.Text.Json.Serialization;

namespace RigForge.Core.Models;

/// <summary>
/// Active lighting effect and its parameters. Effect-specific values live in <see cref="Parameters"/>
/// as raw JSON, described by the effect's parameter schema in the application layer.
/// Colors are "#RRGGBB" strings for a stable wire/disk format.
/// </summary>
[JsonConverter(typeof(EffectSettingsJsonConverter))]
public sealed record EffectSettings
{
    public string EffectId { get; init; } = "rainbow-wave";

    /// <summary>Global brightness, 0..1.</summary>
    public float Brightness { get; init; } = 1f;

    /// <summary>Effect-specific parameter values by key, validated against the effect's schema.</summary>
    public IReadOnlyDictionary<string, JsonElement> Parameters { get; init; } = new Dictionary<string, JsonElement>();

    public int TargetFps { get; init; } = 60;
}

/// <summary>
/// Reads both the current format (<c>parameters</c> object) and the schemaVersion 1 format, where
/// effect parameters were flat fields (<c>speed</c>, <c>colors</c>, <c>angle</c>, <c>sensorSelector</c>,
/// <c>temperatureLow</c>, <c>temperatureHigh</c>) on the settings object itself. Legacy fields are
/// folded into <see cref="EffectSettings.Parameters"/> so old profile.json files migrate transparently.
/// Writing always emits the current format.
/// </summary>
public sealed class EffectSettingsJsonConverter : JsonConverter<EffectSettings>
{
    private static readonly string[] LegacyKeys = ["speed", "colors", "angle", "sensorSelector", "temperatureLow", "temperatureHigh"];

    public override EffectSettings Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return new EffectSettings();
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new EffectSettings();
        }

        var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (root.TryGetProperty("parameters", out var parametersElement) && parametersElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in parametersElement.EnumerateObject())
            {
                parameters[property.Name] = property.Value.Clone();
            }
        }

        foreach (var legacyKey in LegacyKeys)
        {
            if (!parameters.ContainsKey(legacyKey)
                && TryGetPropertyIgnoreCase(root, legacyKey, out var legacyValue)
                && legacyValue.ValueKind != JsonValueKind.Null)
            {
                parameters[legacyKey] = legacyValue.Clone();
            }
        }

        return new EffectSettings
        {
            EffectId = GetString(root, "effectId") ?? "rainbow-wave",
            Brightness = GetFloat(root, "brightness") ?? 1f,
            TargetFps = GetInt(root, "targetFps") ?? 60,
            Parameters = parameters,
        };
    }

    public override void Write(Utf8JsonWriter writer, EffectSettings value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("effectId", value.EffectId);
        writer.WriteNumber("brightness", value.Brightness);
        writer.WriteNumber("targetFps", value.TargetFps);
        writer.WritePropertyName("parameters");
        writer.WriteStartObject();
        if (value.Parameters is not null)
        {
            foreach (var (key, element) in value.Parameters)
            {
                writer.WritePropertyName(key);
                element.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement root, string name) =>
        TryGetPropertyIgnoreCase(root, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static float? GetFloat(JsonElement root, string name) =>
        TryGetPropertyIgnoreCase(root, name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var result)
            ? result
            : null;

    private static int? GetInt(JsonElement root, string name) =>
        TryGetPropertyIgnoreCase(root, name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : null;
}
