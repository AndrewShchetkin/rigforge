namespace RigForge.Core.Models;

/// <summary>
/// Caller-owned, reusable buffer for a downscaled screen capture (BGRA8888, stride = Width * 4).
/// Reusing it keeps the 60 FPS Ambilight path allocation-free.
/// </summary>
public sealed class ScreenFrameBuffer
{
    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Monotonic frame number; 0 means "no frame yet".</summary>
    public long Sequence { get; private set; }

    public byte[] Pixels { get; private set; } = [];

    public void CopyFrom(int width, int height, long sequence, ReadOnlySpan<byte> bgra)
    {
        var length = width * height * 4;
        if (bgra.Length < length)
        {
            throw new ArgumentException("Pixel data is smaller than width * height * 4.", nameof(bgra));
        }

        if (Pixels.Length != length)
        {
            Pixels = new byte[length];
        }

        bgra[..length].CopyTo(Pixels);
        Width = width;
        Height = height;
        Sequence = sequence;
    }
}
