using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Application.Effects.Library;

/// <summary>
/// Stretches the (GPU-downscaled) desktop image over the canvas, so zones placed around the
/// monitor area of the layout pick up the colors at the matching screen edge.
/// </summary>
internal sealed class AmbilightEffect : IEffect
{
    public const string EffectId = "ambilight";

    public static readonly EffectDescriptor Info = new(
        EffectId, "Ambilight", "Mirrors the screen content; place zones around the canvas like around your monitor.",
        IsAnimated: true, UsesColors: false, UsesSpeed: false, UsesAngle: false, UsesSensor: false, MinColors: 0, MaxColors: 0);

    private readonly IScreenCapturer _capturer;
    private readonly ScreenFrameBuffer _frame = new();

    public AmbilightEffect(IScreenCapturer capturer)
    {
        _capturer = capturer;
        if (capturer.IsSupported)
        {
            capturer.Start();
        }
    }

    public EffectDescriptor Descriptor => Info;

    public void Render(Span<byte> rgba, in EffectFrameContext context)
    {
        _capturer.TryCopyLatestFrame(_frame);

        var srcWidth = _frame.Width;
        var srcHeight = _frame.Height;
        var src = _frame.Pixels.AsSpan();
        if (srcWidth <= 0 || srcHeight <= 0 || src.Length < srcWidth * srcHeight * 4)
        {
            CanvasBuffer.Fill(rgba, RgbColor.Black);
            return;
        }

        var dstWidth = context.Width;
        var dstHeight = context.Height;
        if (dstWidth <= 0 || dstHeight <= 0)
        {
            return;
        }

        for (var dy = 0; dy < dstHeight; dy++)
        {
            var sy = Math.Clamp((dy + 0.5f) * srcHeight / dstHeight - 0.5f, 0f, srcHeight - 1);
            var y0 = (int)sy;
            var y1 = Math.Min(y0 + 1, srcHeight - 1);
            var fy = (int)((sy - y0) * 256f + 0.5f);
            var invFy = 256 - fy;
            var row0 = y0 * srcWidth;
            var row1 = y1 * srcWidth;
            var dstRow = dy * dstWidth * 4;

            for (var dx = 0; dx < dstWidth; dx++)
            {
                var sx = Math.Clamp((dx + 0.5f) * srcWidth / dstWidth - 0.5f, 0f, srcWidth - 1);
                var x0 = (int)sx;
                var x1 = Math.Min(x0 + 1, srcWidth - 1);
                var fx = (int)((sx - x0) * 256f + 0.5f);
                var invFx = 256 - fx;

                var w00 = invFx * invFy;
                var w10 = fx * invFy;
                var w01 = invFx * fy;
                var w11 = fx * fy;

                var p00 = (row0 + x0) * 4;
                var p10 = (row0 + x1) * 4;
                var p01 = (row1 + x0) * 4;
                var p11 = (row1 + x1) * 4;

                var d = dstRow + dx * 4;
                rgba[d] = (byte)((src[p00 + 2] * w00 + src[p10 + 2] * w10 + src[p01 + 2] * w01 + src[p11 + 2] * w11 + 32768) >> 16);
                rgba[d + 1] = (byte)((src[p00 + 1] * w00 + src[p10 + 1] * w10 + src[p01 + 1] * w01 + src[p11 + 1] * w11 + 32768) >> 16);
                rgba[d + 2] = (byte)((src[p00] * w00 + src[p10] * w10 + src[p01] * w01 + src[p11] * w11 + 32768) >> 16);
                rgba[d + 3] = 255;
            }
        }
    }

    public void Dispose()
    {
        _capturer.Stop();
    }
}
