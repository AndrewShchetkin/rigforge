using RigForge.Core.Models;

namespace RigForge.Application.Effects.Library;

/// <summary>Maps a temperature onto the configured color ramp (cold color first, hot color last).</summary>
internal sealed class TemperatureEffect : IEffect
{
    public const string EffectId = "temperature";

    public static readonly EffectDescriptor Info = new(
        EffectId, "Temperature", "Color follows a sensor: cold colors at the low threshold, hot colors at the high one.",
        IsAnimated: false, UsesColors: true, UsesSpeed: false, UsesAngle: false, UsesSensor: true, MinColors: 2, MaxColors: 8);

    private static readonly string DefaultSelector = TelemetrySnapshot.RoleSelector(SensorRole.CpuTemperature);

    public EffectDescriptor Descriptor => Info;

    public void Render(Span<byte> rgba, in EffectFrameContext context)
    {
        var settings = context.Settings;
        if (!context.Telemetry.TryResolve(settings.SensorSelector ?? DefaultSelector, out var reading) || context.Colors.Count == 0)
        {
            CanvasBuffer.Fill(rgba, context.Color(0));
            return;
        }

        var span = Math.Max(settings.TemperatureHigh - settings.TemperatureLow, 0.1f);
        var position = Math.Clamp((reading.Value - settings.TemperatureLow) / span, 0f, 1f) * (context.Colors.Count - 1);
        var index = Math.Min((int)position, context.Colors.Count - 2);

        if (index < 0)
        {
            CanvasBuffer.Fill(rgba, context.Color(0));
            return;
        }

        var from = context.Colors[index];
        var to = context.Colors[index + 1];
        var t = position - index;
        CanvasBuffer.Fill(rgba, new RgbColor(
            (byte)(from.R + (to.R - from.R) * t),
            (byte)(from.G + (to.G - from.G) * t),
            (byte)(from.B + (to.B - from.B) * t)));
    }

    public void Dispose()
    {
    }
}
