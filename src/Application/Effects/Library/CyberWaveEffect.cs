using RigForge.Core.Models;

namespace RigForge.Application.Effects.Library;

/// <summary>Two neon ribbons racing in opposite directions.</summary>
internal sealed class CyberWaveEffect : ProceduralEffect
{
    public const string EffectId = "cyber-wave";

    public static readonly EffectDescriptor Info = new(
        EffectId,
        "Cyber waves",
        "Two neon ribbons race in opposite directions with sharp specular crests.",
        IsAnimated: true,
        Parameters:
        [
            EffectParameterDefinition.ColorList("colors", "Colors", ["#00F5FF", "#FF00A0", "#120458"], minColors: 2, maxColors: 6),
            EffectParameterDefinition.FloatRange("speed", "Speed", defaultValue: 1, min: 0.1, max: 6, step: 0.1, unit: "×"),
            EffectParameterDefinition.FloatRange("angle", "Direction", defaultValue: 30, min: -360, max: 360, step: 1, unit: "°"),
            EffectParameterDefinition.FloatRange("sharpness", "Sharpness", defaultValue: 1.8, min: 0.5, max: 4, step: 0.1),
        ],
        Category: EffectCategory.Flowing);

    public override EffectDescriptor Descriptor => Info;

    protected override RgbColor Shade(float u, float v, in EffectFrameContext context)
    {
        var sharpness = Math.Clamp(context.GetFloat("sharpness", 1.8f), 0.5f, 8f);
        var time = (float)context.Time * context.Speed;
        var along = PaletteHelper.Project(u, v, context.Angle, out var across);
        var waveA = MathF.Sin((along * 14f - time * 2.4f) * MathF.PI);
        var waveB = MathF.Sin((along * 11f + time * 2.1f + across * 3f) * MathF.PI);
        var crestA = MathF.Pow(Math.Clamp(waveA, 0f, 1f), sharpness);
        var crestB = MathF.Pow(Math.Clamp(waveB, 0f, 1f), sharpness);
        var specular = MathF.Pow(Math.Clamp(waveA * waveB, 0f, 1f), sharpness * 0.55f);
        var t = PaletteHelper.Frac(0.5f + 0.35f * waveA + 0.25f * waveB);
        var color = PaletteHelper.SampleGradient(context.Colors, t, wrap: true);
        var intensity = Math.Clamp(0.05f + crestA * 0.78f + crestB * 0.62f + specular * 0.7f, 0f, 1f);
        return color.Scale(intensity);
    }
}
