using RigForge.Core.Models;

namespace RigForge.Application.Effects.Library;

/// <summary>Rising flames from analytical value noise. Hotter at the bottom, cooled toward the top.</summary>
internal sealed class FireLavaEffect : ProceduralEffect
{
    public const string EffectId = "fire-lava";

    public static readonly EffectDescriptor Info = new(
        EffectId,
        "Inferno",
        "Rising flames, hotter at the base, with flickering tongues.",
        IsAnimated: true,
        Parameters:
        [
            EffectParameterDefinition.ColorList("colors", "Colors", ["#120000", "#B81400", "#FF5E00", "#FFD000"], minColors: 3, maxColors: 5),
            EffectParameterDefinition.FloatRange("speed", "Speed", defaultValue: 1.2, min: 0.2, max: 5, step: 0.1, unit: "×"),
            EffectParameterDefinition.FloatRange("intensity", "Intensity", defaultValue: 1, min: 0.3, max: 2, step: 0.1),
            EffectParameterDefinition.FloatRange("cooling", "Cooling", defaultValue: 0.9, min: 0.2, max: 2, step: 0.1),
        ],
        Category: EffectCategory.Flowing);

    public override EffectDescriptor Descriptor => Info;

    protected override RgbColor Shade(float u, float v, in EffectFrameContext context)
    {
        var intensity = MathF.Max(context.GetFloat("intensity", 1f), 0f);
        var cooling = Math.Clamp(context.GetFloat("cooling", 0.9f), 0.05f, 4f);
        var time = (float)context.Time * MathF.Max(context.Speed, 0f);
        var rise = (1f - v) * 2.8f + time * 1.25f;
        var n1 = PaletteHelper.ValueNoise(u * 3.5f + 2f, rise, 0.4f);
        var n2 = PaletteHelper.ValueNoise(u * 7f, rise * 1.7f + 5f, 2.2f);
        var n3 = PaletteHelper.ValueNoise(u * 15f + 1f, rise * 2.6f, 6.5f + time * 0.15f);
        var flicker = 0.58f + 0.42f * (n1 * 0.62f + n2 * 0.28f + n3 * 0.1f);
        var vertical = MathF.Pow(Math.Clamp(v, 0f, 1f), cooling);
        var tongue = MathF.Max(0f, n1 - 0.62f) * (1f - v) * 0.55f;
        var heat = Math.Clamp(vertical * flicker * intensity + tongue * intensity, 0f, 1f);
        return PaletteHelper.SampleGradient(context.Colors, heat, wrap: false);
    }
}
