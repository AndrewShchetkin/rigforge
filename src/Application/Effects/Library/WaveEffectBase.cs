using SkiaSharp;

namespace RigForge.Application.Effects.Library;

/// <summary>A repeating linear gradient that scrolls across the canvas in the direction of <c>Angle</c>.</summary>
internal abstract class WaveEffectBase : IEffect
{
    private readonly SKPaint _paint = new() { IsAntialias = false };

    public abstract EffectDescriptor Descriptor { get; }

    public void Render(SKCanvas canvas, in EffectFrameContext context)
    {
        var wavelength = MathF.Max(context.Width, context.Height);
        var offset = (float)(context.Time * context.Settings.Speed * wavelength * 0.25 % wavelength);

        var localMatrix = SKMatrix.CreateTranslation(offset, 0)
            .PostConcat(SKMatrix.CreateRotationDegrees(context.Settings.Angle, context.Width / 2f, context.Height / 2f));

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(wavelength, 0),
            GetStops(context),
            null,
            SKShaderTileMode.Repeat,
            localMatrix);

        _paint.Shader = shader;
        canvas.DrawRect(0, 0, context.Width, context.Height, _paint);
        _paint.Shader = null;
    }

    public void Dispose() => _paint.Dispose();

    /// <summary>Gradient stops; the first and last color must match for a seamless repeat.</summary>
    protected abstract SKColor[] GetStops(in EffectFrameContext context);
}

internal sealed class RainbowWaveEffect : WaveEffectBase
{
    public const string EffectId = "rainbow-wave";

    public static readonly EffectDescriptor Info = new(
        EffectId, "Rainbow wave", "Full spectrum scrolling across the canvas.",
        IsAnimated: true, UsesColors: false, UsesSpeed: true, UsesAngle: true, UsesSensor: false, MinColors: 0, MaxColors: 0);

    private static readonly SKColor[] Spectrum = Enumerable.Range(0, 13).Select(i => SKColor.FromHsv(i * 30 % 360, 100, 100)).ToArray();

    public override EffectDescriptor Descriptor => Info;

    protected override SKColor[] GetStops(in EffectFrameContext context) => Spectrum;
}

internal sealed class GradientWaveEffect : WaveEffectBase
{
    public const string EffectId = "gradient";

    public static readonly EffectDescriptor Info = new(
        EffectId, "Gradient", "Your colors blended into a moving gradient.",
        IsAnimated: true, UsesColors: true, UsesSpeed: true, UsesAngle: true, UsesSensor: false, MinColors: 2, MaxColors: 8);

    private IReadOnlyList<SKColor>? _source;
    private SKColor[] _stops = [];

    public override EffectDescriptor Descriptor => Info;

    protected override SKColor[] GetStops(in EffectFrameContext context)
    {
        if (!ReferenceEquals(_source, context.Colors))
        {
            _source = context.Colors;
            var colors = context.Colors.Count > 0 ? context.Colors : [SKColors.White];
            _stops = [.. colors, colors[0]];
        }

        return _stops;
    }
}
