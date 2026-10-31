using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using SkiaSharp;

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

    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    private readonly IScreenCapturer _capturer;
    private readonly ScreenFrameBuffer _frame = new();
    private SKImage? _image;

    public AmbilightEffect(IScreenCapturer capturer)
    {
        _capturer = capturer;
        if (capturer.IsSupported)
        {
            capturer.Start();
        }
    }

    public EffectDescriptor Descriptor => Info;

    public void Render(SKCanvas canvas, in EffectFrameContext context)
    {
        if (_capturer.TryCopyLatestFrame(_frame))
        {
            _image?.Dispose();
            var info = new SKImageInfo(_frame.Width, _frame.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            _image = SKImage.FromPixelCopy(info, _frame.Pixels, info.RowBytes);
        }

        if (_image is null)
        {
            canvas.Clear(SKColors.Black);
            return;
        }

        canvas.DrawImage(_image, new SKRect(0, 0, context.Width, context.Height), Sampling);
    }

    public void Dispose()
    {
        _capturer.Stop();
        _image?.Dispose();
    }
}
