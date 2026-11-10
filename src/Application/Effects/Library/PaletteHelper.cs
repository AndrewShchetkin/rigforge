using System.Runtime.CompilerServices;
using RigForge.Core.Models;

namespace RigForge.Application.Effects.Library;

/// <summary>Allocation-free palette, projection, and value-noise helpers shared by the built-in effects.</summary>
internal static class PaletteHelper
{
    /// <summary>
    /// Samples <paramref name="colors"/> at <paramref name="t"/>. <paramref name="wrap"/> repeats the ramp;
    /// otherwise <paramref name="t"/> is clamped to the first and last stop. Uses the indexer only.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RgbColor SampleGradient(IReadOnlyList<RgbColor> colors, float t, bool wrap)
    {
        var count = colors.Count;
        if (count <= 0)
        {
            return RgbColor.Black;
        }

        if (count == 1 || !float.IsFinite(t))
        {
            return colors[0];
        }

        if (wrap)
        {
            t -= MathF.Floor(t);
            if (t < 0f)
            {
                t += 1f;
            }

            var scaled = t * count;
            var index = (int)scaled;
            if ((uint)index >= (uint)count)
            {
                return colors[0];
            }

            var next = index + 1;
            if (next >= count)
            {
                next = 0;
            }

            return RgbColor.Lerp(colors[index], colors[next], scaled - index);
        }

        if (t <= 0f)
        {
            return colors[0];
        }

        if (t >= 1f)
        {
            return colors[count - 1];
        }

        var position = t * (count - 1);
        var lower = (int)position;
        if (lower >= count - 1)
        {
            return colors[count - 1];
        }

        return RgbColor.Lerp(colors[lower], colors[lower + 1], position - lower);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Frac(float value)
    {
        var wrapped = value - MathF.Floor(value);
        return wrapped < 0f ? wrapped + 1f : wrapped;
    }

    /// <summary>Projects the canvas point onto the unit axis at <paramref name="angleDegrees"/> and the perpendicular.</summary>
    public static float Project(float u, float v, float angleDegrees, out float across)
    {
        var (sin, cos) = MathF.SinCos(angleDegrees * (MathF.PI / 180f));
        var x = u - 0.5f;
        var y = v - 0.5f;
        across = y * cos - x * sin;
        return x * cos + y * sin;
    }

    /// <summary>Maps the projection of <paramref name="angleDegrees"/> across the canvas to 0..1.</summary>
    public static float ProjectUnit(float u, float v, float angleDegrees)
    {
        var along = Project(u, v, angleDegrees, out _);
        var (sin, cos) = MathF.SinCos(angleDegrees * (MathF.PI / 180f));
        var extent = 0.5f * (MathF.Abs(sin) + MathF.Abs(cos));
        if (extent < 0.001f)
        {
            extent = 0.001f;
        }

        return Math.Clamp(along / (extent * 2f) + 0.5f, 0f, 1f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Hash(int x, int y, int z)
    {
        unchecked
        {
            var n = (uint)x * 374761393u + (uint)y * 668265263u + (uint)z * 1440662683u + 1013904223u;
            n = (n ^ (n >> 13)) * 1274126177u;
            return n ^ (n >> 16);
        }
    }

    /// <summary>Smooth 3D value noise in 0..1. Deterministic and stateless.</summary>
    public static float ValueNoise(float x, float y, float z)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var z0 = (int)MathF.Floor(z);
        var tx = Fade(x - x0);
        var ty = Fade(y - y0);
        var tz = Fade(z - z0);
        var x1 = x0 + 1;
        var y1 = y0 + 1;
        var z1 = z0 + 1;

        var n000 = Hash01(x0, y0, z0);
        var n100 = Hash01(x1, y0, z0);
        var n010 = Hash01(x0, y1, z0);
        var n110 = Hash01(x1, y1, z0);
        var n001 = Hash01(x0, y0, z1);
        var n101 = Hash01(x1, y0, z1);
        var n011 = Hash01(x0, y1, z1);
        var n111 = Hash01(x1, y1, z1);

        var nx00 = Lerp(n000, n100, tx);
        var nx10 = Lerp(n010, n110, tx);
        var nx01 = Lerp(n001, n101, tx);
        var nx11 = Lerp(n011, n111, tx);
        var nxy0 = Lerp(nx00, nx10, ty);
        var nxy1 = Lerp(nx01, nx11, ty);
        return Lerp(nxy0, nxy1, tz);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Hash01(int x, int y, int z) => (Hash(x, y, z) & 0xFFFFFF) * (1f / 16777215f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Fade(float t) => t * t * (3f - 2f * t);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Lerp(float from, float to, float t) => from + (to - from) * t;
}
