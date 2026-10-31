using RigForge.Core.Models;
using SkiaSharp;

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

    public void Render(SKCanvas canvas, in EffectFrameContext context)
    {
        var settings = context.Settings;
        if (!context.Telemetry.TryResolve(settings.SensorSelector ?? DefaultSelector, out var reading) || context.Colors.Count == 0)
        {
            canvas.Clear(context.Color(0));
            return;
        }

        var span = Math.Max(settings.TemperatureHigh - settings.TemperatureLow, 0.1f);
        var position = Math.Clamp((reading.Value - settings.TemperatureLow) / span, 0f, 1f) * (context.Colors.Count - 1);
        var index = Math.Min((int)position, context.Colors.Count - 2);

        if (index < 0)
        {
            canvas.Clear(context.Color(0));
            return;
        }

        var from = context.Colors[index];
        var to = context.Colors[index + 1];
        var t = position - index;
        canvas.Clear(new SKColor(
            (byte)(from.Red + (to.Red - from.Red) * t),
            (byte)(from.Green + (to.Green - from.Green) * t),
            (byte)(from.Blue + (to.Blue - from.Blue) * t)));
    }

    public void Dispose()
    {
    }
}
