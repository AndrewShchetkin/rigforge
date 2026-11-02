using RigForge.Core.Models;

namespace RigForge.Application.Effects.Library;

internal sealed class SolidColorEffect : IEffect
{
    public const string EffectId = "solid";

    public static readonly EffectDescriptor Info = new(
        EffectId, "Solid color", "One static color on every LED.",
        IsAnimated: false,
        Parameters:
        [
            EffectParameterDefinition.ColorList("colors", "Color", ["#FF0080"], minColors: 1, maxColors: 1),
        ]);

    public EffectDescriptor Descriptor => Info;

    private static RgbColor ComputeColor(in EffectFrameContext context) => context.Color(0);

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

internal sealed class BreathingEffect : IEffect
{
    public const string EffectId = "breathing";

    public static readonly EffectDescriptor Info = new(
        EffectId, "Breathing", "Fades in and out, stepping through your colors.",
        IsAnimated: true,
        Parameters:
        [
            EffectParameterDefinition.ColorList("colors", "Colors", ["#FF0080", "#00C8FF"], minColors: 1, maxColors: 8),
            EffectParameterDefinition.FloatRange("speed", "Speed", defaultValue: 1, min: 0.05, max: 10, step: 0.1, unit: "×"),
        ]);

    private const double PeriodSeconds = 4.0;

    public EffectDescriptor Descriptor => Info;

    private static RgbColor ComputeColor(in EffectFrameContext context)
    {
        var phase = context.Time * Math.Max(context.Speed, 0.01f) / PeriodSeconds;
        var cycle = (long)Math.Floor(phase);
        var intensity = (float)((1 - Math.Cos((phase - cycle) * 2 * Math.PI)) / 2);
        var color = context.Color((int)(cycle % Math.Max(context.Colors.Count, 1)));

        return new RgbColor((byte)(color.R * intensity), (byte)(color.G * intensity), (byte)(color.B * intensity));
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

internal sealed class SpectrumCycleEffect : IEffect
{
    public const string EffectId = "spectrum-cycle";

    public static readonly EffectDescriptor Info = new(
        EffectId, "Spectrum cycle", "All LEDs slowly travel through the color wheel together.",
        IsAnimated: true,
        Parameters:
        [
            EffectParameterDefinition.FloatRange("speed", "Speed", defaultValue: 1, min: 0.05, max: 10, step: 0.1, unit: "×"),
        ]);

    private const double DegreesPerSecond = 36.0;

    public EffectDescriptor Descriptor => Info;

    private static RgbColor ComputeColor(in EffectFrameContext context)
    {
        var hue = (float)(context.Time * context.Speed * DegreesPerSecond % 360.0);
        return CanvasBuffer.FromHsv(hue, 100, 100);
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
