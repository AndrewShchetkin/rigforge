using System.Globalization;

namespace RigForge.Core.Models;

/// <summary>A 24-bit sRGB color as sent to LED controllers.</summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static RgbColor Black => default;

    public static RgbColor White => new(255, 255, 255);

    /// <summary>Scales all channels by <paramref name="factor"/> (clamped to 0..1).</summary>
    public RgbColor Scale(float factor)
    {
        factor = Math.Clamp(factor, 0f, 1f);
        return new RgbColor((byte)(R * factor + 0.5f), (byte)(G * factor + 0.5f), (byte)(B * factor + 0.5f));
    }

    public static RgbColor Lerp(RgbColor from, RgbColor to, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new RgbColor(
            (byte)(from.R + (to.R - from.R) * t + 0.5f),
            (byte)(from.G + (to.G - from.G) * t + 0.5f),
            (byte)(from.B + (to.B - from.B) * t + 0.5f));
    }

    /// <summary>Creates a color from HSV components (hue in degrees, saturation and value in 0..1).</summary>
    public static RgbColor FromHsv(float hue, float saturation, float value)
    {
        hue = ((hue % 360f) + 360f) % 360f;
        saturation = Math.Clamp(saturation, 0f, 1f);
        value = Math.Clamp(value, 0f, 1f);

        var chroma = value * saturation;
        var x = chroma * (1f - MathF.Abs(hue / 60f % 2f - 1f));
        var m = value - chroma;

        var (r, g, b) = (int)(hue / 60f) switch
        {
            0 => (chroma, x, 0f),
            1 => (x, chroma, 0f),
            2 => (0f, chroma, x),
            3 => (0f, x, chroma),
            4 => (x, 0f, chroma),
            _ => (chroma, 0f, x),
        };

        return new RgbColor(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    public static bool TryParse(string? text, out RgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '#')
        {
            span = span[1..];
        }

        if (span.Length != 6 || !uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        color = new RgbColor((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    public static RgbColor Parse(string text) =>
        TryParse(text, out var color) ? color : throw new FormatException($"'{text}' is not a #RRGGBB color.");

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";

    private static byte ToByte(float channel) => (byte)Math.Clamp(channel * 255f + 0.5f, 0f, 255f);
}
