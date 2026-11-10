using System.Runtime.InteropServices;
using RigForge.Core.Models;

namespace RigForge.Application.Effects.Library;

/// <summary>A repeating linear gradient that scrolls across the canvas in the direction of <c>Angle</c>.</summary>
internal abstract class WaveEffectBase : IEffect
{
    private readonly uint[] _lut = new uint[256];
    private readonly RgbColor[] _colorLut = new RgbColor[256];
    private RgbColor[]? _cachedStops;

    public abstract EffectDescriptor Descriptor { get; }

    public void Render(Span<byte> rgba, in EffectFrameContext context)
    {
        var width = context.Width;
        var height = context.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        EnsureLut(in context);

        var wavelength = MathF.Max(width, height);
        var offset = (float)(context.Time * context.Speed * wavelength * 0.25 % wavelength);
        var (sin, cos) = MathF.SinCos(context.Angle * (MathF.PI / 180f));

        var cx = width / 2f;
        var cy = height / 2f;
        var scale = 256f / wavelength;
        var stepXFp = (int)MathF.Round(cos * scale * 65536f);

        var pixels = MemoryMarshal.Cast<byte, uint>(rgba);
        var lut = _lut;

        for (var y = 0; y < height; y++)
        {
            var rowStart = (cx + (0.5f - cx) * cos + (y + 0.5f - cy) * sin - offset) * scale;
            var phaseFp = (int)MathF.Floor(rowStart * 65536f);
            var row = pixels.Slice(y * width, width);

            for (var x = 0; x < width; x++)
            {
                row[x] = lut[(phaseFp >> 16) & 255];
                phaseFp += stepXFp;
            }
        }
    }

    public bool TrySamplePoints(ReadOnlySpan<LedPoint> points, Span<RgbColor> destination, in EffectFrameContext context)
    {
        var width = context.Width;
        var height = context.Height;
        if (width <= 0 || height <= 0)
        {
            destination.Fill(RgbColor.Black);
            return true;
        }

        EnsureLut(in context);

        var wavelength = MathF.Max(width, height);
        var offset = (float)(context.Time * context.Speed * wavelength * 0.25 % wavelength);
        var (sin, cos) = MathF.SinCos(context.Angle * (MathF.PI / 180f));

        var cx = width / 2f;
        var cy = height / 2f;
        var scale = 256f / wavelength;
        var lut = _colorLut;

        var count = Math.Min(points.Length, destination.Length);
        for (var i = 0; i < count; i++)
        {
            var point = points[i];
            // Same phase formula as Render, evaluated at the LED position instead of every canvas pixel.
            var phase = (cx + (point.X - cx) * cos + (point.Y - cy) * sin - offset) * scale;
            destination[i] = lut[(int)MathF.Floor(phase) & 255];
        }

        return true;
    }

    public void Dispose()
    {
    }

    /// <summary>Gradient stops; the first and last color must match for a seamless repeat.</summary>
    protected abstract RgbColor[] GetStops(in EffectFrameContext context);

    private void EnsureLut(in EffectFrameContext context)
    {
        var stops = GetStops(in context);
        if (!ReferenceEquals(stops, _cachedStops))
        {
            _cachedStops = stops;
            RebuildLut(stops);
        }
    }

    private void RebuildLut(RgbColor[] stops)
    {
        if (stops.Length == 0)
        {
            Array.Fill(_colorLut, RgbColor.White);
            Array.Fill(_lut, CanvasBuffer.PackRgba(RgbColor.White));
            return;
        }

        if (stops.Length == 1)
        {
            Array.Fill(_colorLut, stops[0]);
            Array.Fill(_lut, CanvasBuffer.PackRgba(stops[0]));
            return;
        }

        var segments = stops.Length - 1;
        for (var i = 0; i < _lut.Length; i++)
        {
            var pos = i / 255f * segments;
            var index = Math.Min((int)pos, segments - 1);
            var t = pos - index;
            var color = RgbColor.Lerp(stops[index], stops[index + 1], t);
            _colorLut[i] = color;
            _lut[i] = CanvasBuffer.PackRgba(color);
        }
    }
}

internal sealed class RainbowWaveEffect : WaveEffectBase
{
    public const string EffectId = "rainbow-wave";

    public static readonly EffectDescriptor Info = new(
        EffectId, "Rainbow wave", "Full spectrum scrolling across the canvas.",
        IsAnimated: true,
        Parameters:
        [
            EffectParameterDefinition.FloatRange("speed", "Speed", defaultValue: 1, min: 0.05, max: 10, step: 0.1, unit: "×"),
            EffectParameterDefinition.FloatRange("angle", "Direction", defaultValue: 0, min: -360, max: 360, step: 1, unit: "°"),
        ],
        Category: EffectCategory.Flowing);

    private static readonly RgbColor[] Spectrum = Enumerable.Range(0, 13).Select(i => CanvasBuffer.FromHsv(i * 30 % 360, 100, 100)).ToArray();

    public override EffectDescriptor Descriptor => Info;

    protected override RgbColor[] GetStops(in EffectFrameContext context) => Spectrum;
}

internal sealed class GradientWaveEffect : WaveEffectBase
{
    public const string EffectId = "gradient";

    public static readonly EffectDescriptor Info = new(
        EffectId, "Gradient", "Your colors blended into a moving gradient.",
        IsAnimated: true,
        Parameters:
        [
            EffectParameterDefinition.ColorList("colors", "Colors", ["#FF0080", "#00C8FF"], minColors: 2, maxColors: 8),
            EffectParameterDefinition.FloatRange("speed", "Speed", defaultValue: 1, min: 0.05, max: 10, step: 0.1, unit: "×"),
            EffectParameterDefinition.FloatRange("angle", "Direction", defaultValue: 0, min: -360, max: 360, step: 1, unit: "°"),
        ],
        Category: EffectCategory.Flowing);

    private IReadOnlyList<RgbColor>? _source;
    private RgbColor[] _stops = [];

    public override EffectDescriptor Descriptor => Info;

    protected override RgbColor[] GetStops(in EffectFrameContext context)
    {
        if (!ReferenceEquals(_source, context.Colors))
        {
            _source = context.Colors;
            var colors = context.Colors.Count > 0 ? context.Colors : [RgbColor.White];
            _stops = [.. colors, colors[0]];
        }

        return _stops;
    }
}
