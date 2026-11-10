using System.Runtime.InteropServices;
using RigForge.Core.Models;

namespace RigForge.Application.Effects.Library;

/// <summary>
/// Shades a normalized canvas coordinate directly. The preview raster and the LED path call the same
/// function, so they match, and neither call allocates.
/// </summary>
internal abstract class ProceduralEffect : IEffect
{
    public abstract EffectDescriptor Descriptor { get; }

    public void Render(Span<byte> rgba, in EffectFrameContext context)
    {
        var width = Math.Max(1, context.Width);
        var height = Math.Max(1, context.Height);
        var count64 = (long)width * height;
        if (count64 <= 0 || count64 > int.MaxValue || rgba.Length < count64 * 4)
        {
            return;
        }

        var count = (int)count64;
        var pixels = MemoryMarshal.Cast<byte, uint>(rgba[..(count * 4)]);
        var invWidth = 1f / width;
        var invHeight = 1f / height;
        for (var y = 0; y < height; y++)
        {
            var v = (y + 0.5f) * invHeight;
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                pixels[row + x] = CanvasBuffer.PackRgba(Shade((x + 0.5f) * invWidth, v, in context));
            }
        }
    }

    public bool TrySamplePoints(ReadOnlySpan<LedPoint> points, Span<RgbColor> destination, in EffectFrameContext context)
    {
        var width = Math.Max(1, context.Width);
        var height = Math.Max(1, context.Height);
        var invWidth = 1f / width;
        var invHeight = 1f / height;
        var count = Math.Min(points.Length, destination.Length);
        for (var i = 0; i < count; i++)
        {
            var point = points[i];
            destination[i] = Shade(point.X * invWidth, point.Y * invHeight, in context);
        }

        return true;
    }

    public void Dispose()
    {
    }

    protected abstract RgbColor Shade(float u, float v, in EffectFrameContext context);
}
