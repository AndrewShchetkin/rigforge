using SkiaSharp;

namespace RigForge.Application.Effects.Library;

internal sealed class SolidColorEffect : IEffect
{
    public const string EffectId = "solid";

    public static readonly EffectDescriptor Info = new(
        EffectId, "Solid color", "One static color on every LED.",
        IsAnimated: false, UsesColors: true, UsesSpeed: false, UsesAngle: false, UsesSensor: false, MinColors: 1, MaxColors: 1);

    public EffectDescriptor Descriptor => Info;

    public void Render(SKCanvas canvas, in EffectFrameContext context) => canvas.Clear(context.Color(0));

    public void Dispose()
    {
    }
}

internal sealed class BreathingEffect : IEffect
{
    public const string EffectId = "breathing";

    public static readonly EffectDescriptor Info = new(
        EffectId, "Breathing", "Fades in and out, stepping through your colors.",
        IsAnimated: true, UsesColors: true, UsesSpeed: true, UsesAngle: false, UsesSensor: false, MinColors: 1, MaxColors: 8);

    private const double PeriodSeconds = 4.0;

    public EffectDescriptor Descriptor => Info;

    public void Render(SKCanvas canvas, in EffectFrameContext context)
    {
        var phase = context.Time * Math.Max(context.Settings.Speed, 0.01f) / PeriodSeconds;
        var cycle = (long)Math.Floor(phase);
        var intensity = (float)((1 - Math.Cos((phase - cycle) * 2 * Math.PI)) / 2);
        var color = context.Color((int)(cycle % Math.Max(context.Colors.Count, 1)));

        canvas.Clear(new SKColor((byte)(color.Red * intensity), (byte)(color.Green * intensity), (byte)(color.Blue * intensity)));
    }

    public void Dispose()
    {
    }
}

internal sealed class SpectrumCycleEffect : IEffect
{
    public const string EffectId = "spectrum-cycle";

    public static readonly EffectDescriptor Info = new(
        EffectId, "Spectrum cycle", "All LEDs slowly travel through the color wheel together.",
        IsAnimated: true, UsesColors: false, UsesSpeed: true, UsesAngle: false, UsesSensor: false, MinColors: 0, MaxColors: 0);

    private const double DegreesPerSecond = 36.0;

    public EffectDescriptor Descriptor => Info;

    public void Render(SKCanvas canvas, in EffectFrameContext context)
    {
        var hue = (float)(context.Time * context.Settings.Speed * DegreesPerSecond % 360.0);
        canvas.Clear(SKColor.FromHsv(hue, 100, 100));
    }

    public void Dispose()
    {
    }
}
