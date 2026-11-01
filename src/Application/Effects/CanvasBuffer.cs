using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RigForge.Core.Models;

namespace RigForge.Application.Effects;

internal static class CanvasBuffer
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint PackRgba(RgbColor color)
    {
        uint packed = (uint)color.R | ((uint)color.G << 8) | ((uint)color.B << 16) | 0xFF000000u;
        return BitConverter.IsLittleEndian ? packed : BinaryPrimitives.ReverseEndianness(packed);
    }

    public static void Fill(Span<byte> rgba, RgbColor color) =>
        MemoryMarshal.Cast<byte, uint>(rgba).Fill(PackRgba(color));

    public static RgbColor FromHsv(float hueDegrees, float saturationPercent, float valuePercent)
    {
        var h = (hueDegrees % 360f + 360f) % 360f;
        var s = Math.Clamp(saturationPercent / 100f, 0f, 1f);
        var v = Math.Clamp(valuePercent / 100f, 0f, 1f);

        var c = v * s;
        var x = c * (1f - MathF.Abs(h / 60f % 2f - 1f));
        var m = v - c;

        var (r1, g1, b1) = h switch
        {
            < 60f => (c, x, 0f),
            < 120f => (x, c, 0f),
            < 180f => (0f, c, x),
            < 240f => (0f, x, c),
            < 300f => (x, 0f, c),
            _ => (c, 0f, x),
        };

        return new RgbColor(
            (byte)Math.Clamp((int)((r1 + m) * 255f + 0.5f), 0, 255),
            (byte)Math.Clamp((int)((g1 + m) * 255f + 0.5f), 0, 255),
            (byte)Math.Clamp((int)((b1 + m) * 255f + 0.5f), 0, 255));
    }
}
