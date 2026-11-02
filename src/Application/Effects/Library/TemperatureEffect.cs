using RigForge.Core.Models;

namespace RigForge.Application.Effects.Library;

/// <summary>Maps a temperature onto the configured color ramp (cold color first, hot color last).</summary>
internal sealed class TemperatureEffect : IEffect
{
    public const string EffectId = "temperature";

    public static readonly EffectDescriptor Info = new(
        EffectId, "Temperature", "Color follows a sensor: cold colors at the low threshold, hot colors at the high one.",
        IsAnimated: false,
        Parameters:
        [
            EffectParameterDefinition.ColorList("colors", "Colors", ["#00C8FF", "#FF0080"], minColors: 2, maxColors: 8),
            EffectParameterDefinition.SensorSelector("sensorSelector", "Sensor", "role:CpuTemperature"),
            EffectParameterDefinition.FloatRange("temperatureLow", "Cold at", defaultValue: 35, min: 0, max: 120, step: 1, unit: "°C"),
            EffectParameterDefinition.FloatRange("temperatureHigh", "Hot at", defaultValue: 80, min: 0, max: 120, step: 1, unit: "°C"),
        ]);

    private static readonly string DefaultSelector = TelemetrySnapshot.RoleSelector(SensorRole.CpuTemperature);

    public EffectDescriptor Descriptor => Info;

    private static RgbColor ComputeColor(in EffectFrameContext context)
    {
        if (!context.Telemetry.TryResolve(context.SensorSelector ?? DefaultSelector, out var reading) || context.Colors.Count == 0)
        {
            return context.Color(0);
        }

        var span = Math.Max(context.TemperatureHigh - context.TemperatureLow, 0.1f);
        var position = Math.Clamp((reading.Value - context.TemperatureLow) / span, 0f, 1f) * (context.Colors.Count - 1);
        var index = Math.Min((int)position, context.Colors.Count - 2);

        if (index < 0)
        {
            return context.Color(0);
        }

        var from = context.Colors[index];
        var to = context.Colors[index + 1];
        var t = position - index;
        return new RgbColor(
            (byte)(from.R + (to.R - from.R) * t),
            (byte)(from.G + (to.G - from.G) * t),
            (byte)(from.B + (to.B - from.B) * t));
    }

    public void Render(Span<byte> rgba, in EffectFrameContext context) => CanvasBuffer.Fill(rgba, ComputeColor(in context));

    public bool TrySamplePoints(ReadOnlySpan<LedPoint> points, Span<RgbColor> destination, in EffectFrameContext context)
    {
        destination.Fill(ComputeColor(in context));
        return true;
    }

    public void Dispose()
    {
    }
}
