using RigForge.Core.Models;

namespace RigForge.Application.Effects.Library;

/// <summary>A twisted pinwheel rotating around the canvas center.</summary>
internal sealed class SpiralVortexEffect : ProceduralEffect
{
    public const string EffectId = "spiral-vortex";

    public static readonly EffectDescriptor Info = new(
        EffectId,
        "Spiral vortex",
        "A pinwheel vortex twists around the center — built for fans and full-case layouts.",
        IsAnimated: true,
        Parameters:
        [
            EffectParameterDefinition.ColorList("colors", "Colors", ["#FF0080", "#7928CA", "#00DFD8", "#00FF87"], minColors: 2, maxColors: 8),
            EffectParameterDefinition.FloatRange("speed", "Speed", defaultValue: 1.2, min: 0.1, max: 6, step: 0.1, unit: "×"),
            EffectParameterDefinition.IntRange("arms", "Arms", defaultValue: 3, min: 1, max: 8),
            EffectParameterDefinition.FloatRange("twist", "Twist", defaultValue: 2, min: 0, max: 6, step: 0.1),
            EffectParameterDefinition.Boolean("clockwise", "Clockwise", defaultValue: true),
        ],
        Category: EffectCategory.Spatial);

    public override EffectDescriptor Descriptor => Info;

    protected override RgbColor Shade(float u, float v, in EffectFrameContext context)
    {
        var dx = u - 0.5f;
        var dy = v - 0.5f;
        var radius = MathF.Sqrt(dx * dx + dy * dy);
        var angle = MathF.Atan2(dy, dx);
        var arms = Math.Clamp(context.GetInt("arms", 3), 1, 8);
        var twist = context.GetFloat("twist", 2f);
        var direction = context.GetBool("clockwise", true) ? 1f : -1f;
        var theta = angle * direction + radius * twist * MathF.Tau + (float)context.Time * context.Speed;
        var cycles = PaletteHelper.Frac(theta / MathF.Tau * arms);
        var blade = MathF.Pow(MathF.Sin(cycles * MathF.PI), 0.65f);
        var color = PaletteHelper.SampleGradient(context.Colors, cycles, wrap: true);
        var vignette = Math.Clamp(1f - radius * 0.35f, 0.4f, 1f);
        var core = MathF.Exp(-radius * 7.5f);
        var intensity = Math.Clamp((0.18f + 0.82f * blade) * vignette + core * 0.85f, 0f, 1f);
        return color.Scale(intensity);
    }
}
