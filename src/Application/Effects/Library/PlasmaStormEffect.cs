using RigForge.Core.Models;

namespace RigForge.Application.Effects.Library;

/// <summary>Trigonometric plasma mapped onto the user palette.</summary>
internal sealed class PlasmaStormEffect : ProceduralEffect
{
    public const string EffectId = "plasma-storm";

    public static readonly EffectDescriptor Info = new(
        EffectId,
        "Neon plasma",
        "Classic trigonometric plasma mapped onto your palette.",
        IsAnimated: true,
        Parameters:
        [
            EffectParameterDefinition.ColorList("colors", "Colors", ["#FF0080", "#7B2FFF", "#00F5FF", "#FFE600"], minColors: 2, maxColors: 6),
            EffectParameterDefinition.FloatRange("speed", "Speed", defaultValue: 1, min: 0.1, max: 5, step: 0.1, unit: "×"),
            EffectParameterDefinition.FloatRange("scale", "Scale", defaultValue: 1.6, min: 0.5, max: 4, step: 0.1),
        ],
        Category: EffectCategory.Spatial);

    public override EffectDescriptor Descriptor => Info;

    protected override RgbColor Shade(float u, float v, in EffectFrameContext context)
    {
        var scale = MathF.Max(context.GetFloat("scale", 1.6f), 0.05f);
        var time = (float)context.Time * context.Speed;
        var x = u * scale;
        var y = v * scale;
        var v1 = MathF.Sin(x * 9f + time);
        var v2 = MathF.Sin(y * 10f - time * 0.85f);
        var v3 = MathF.Sin((x + y) * 8f + time * 0.6f);
        var dx = x - 0.5f;
        var dy = y - 0.5f;
        var v4 = MathF.Sin(MathF.Sqrt(dx * dx + dy * dy) * 12f - time);
        var plasma = (v1 + v2 + v3 + v4) * 0.25f;
        var t = Math.Clamp(plasma * 0.5f + 0.5f, 0f, 1f);
        var color = PaletteHelper.SampleGradient(context.Colors, t, wrap: false);
        var highlight = MathF.Pow(Math.Clamp(plasma, 0f, 1f), 2.4f);
        var gain = 0.62f + 0.38f * (0.5f + 0.5f * v1) + highlight * 0.2f;
        return color.Scale(gain);
    }
}
