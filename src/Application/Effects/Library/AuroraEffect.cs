using RigForge.Core.Models;

namespace RigForge.Application.Effects.Library;

/// <summary>Folding curtains of northern lights.</summary>
internal sealed class AuroraEffect : ProceduralEffect
{
    public const string EffectId = "aurora";

    public static readonly EffectDescriptor Info = new(
        EffectId,
        "Aurora borealis",
        "Curtains of northern lights fold and drift across the canvas.",
        IsAnimated: true,
        Parameters:
        [
            EffectParameterDefinition.ColorList("colors", "Colors", ["#00FF9D", "#00C8FF", "#7B2FFF", "#062B45"], minColors: 2, maxColors: 6),
            EffectParameterDefinition.FloatRange("speed", "Speed", defaultValue: 1, min: 0.1, max: 5, step: 0.1, unit: "×"),
            EffectParameterDefinition.FloatRange("scale", "Scale", defaultValue: 1.4, min: 0.5, max: 3, step: 0.1),
            EffectParameterDefinition.FloatRange("turbulence", "Turbulence", defaultValue: 0.8, min: 0, max: 2, step: 0.1),
        ],
        Category: EffectCategory.Flowing);

    public override EffectDescriptor Descriptor => Info;

    protected override RgbColor Shade(float u, float v, in EffectFrameContext context)
    {
        var scale = MathF.Max(context.GetFloat("scale", 1.4f), 0.05f);
        var turbulence = context.GetFloat("turbulence", 0.8f);
        var time = (float)context.Time * context.Speed;
        var x = u * scale * 6f;
        var y = v * scale * 3f;
        var warp = MathF.Sin(y * 2.2f + time * 0.7f) * turbulence;
        var curtainA = Fold(x * 1.15f + warp + time * 0.55f);
        var curtainB = Fold(x * 1.8f - warp * 0.8f - time * 0.42f);
        var curtainC = Fold(x * 0.7f + MathF.Cos(y * 1.6f - time * 0.33f) * turbulence + time * 0.2f);
        var peak = 0.28f + 0.1f * MathF.Sin(x * 0.45f + time * 0.3f);
        var sky = v - peak;
        var vertical = MathF.Exp(-(sky * sky) * 9.6f);
        var shimmer = 0.65f + 0.35f * MathF.Sin(x * 3f + y * 8f + time * 1.4f);
        var energy = (curtainA * 0.85f + curtainB * 0.65f + curtainC * 0.45f) * vertical * shimmer;
        var t = PaletteHelper.Frac(u * 0.35f + curtainB * 0.4f + curtainC * 0.25f + time * 0.03f);
        return PaletteHelper.SampleGradient(context.Colors, t, wrap: true).Scale(Math.Clamp(energy * 1.25f, 0f, 1f));
    }

    private static float Fold(float x)
    {
        var s = MathF.Sin(x);
        return s * s;
    }
}
