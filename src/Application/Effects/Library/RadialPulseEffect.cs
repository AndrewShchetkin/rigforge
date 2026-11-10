using RigForge.Core.Models;

namespace RigForge.Application.Effects.Library;

/// <summary>Concentric shockwaves expanding or collapsing from the center.</summary>
internal sealed class RadialPulseEffect : ProceduralEffect
{
    public const string EffectId = "radial-pulse";

    private static readonly EffectSelectOption[] Directions =
    [
        new("outward", "Outward"),
        new("inward", "Inward"),
    ];

    public static readonly EffectDescriptor Info = new(
        EffectId,
        "Event horizon",
        "Concentric shockwaves expand or collapse from the center.",
        IsAnimated: true,
        Parameters:
        [
            EffectParameterDefinition.ColorList("colors", "Colors", ["#FF0055", "#5900FF", "#00E5FF"], minColors: 2, maxColors: 6),
            EffectParameterDefinition.FloatRange("speed", "Speed", defaultValue: 1, min: 0.1, max: 6, step: 0.1, unit: "×"),
            EffectParameterDefinition.IntRange("rings", "Rings", defaultValue: 3, min: 1, max: 8),
            EffectParameterDefinition.Select("direction", "Direction", "outward", Directions),
        ],
        Category: EffectCategory.Spatial);

    public override EffectDescriptor Descriptor => Info;

    protected override RgbColor Shade(float u, float v, in EffectFrameContext context)
    {
        var dx = u - 0.5f;
        var dy = v - 0.5f;
        var radius = MathF.Sqrt(dx * dx + dy * dy) * 2f;
        var rings = Math.Clamp(context.GetInt("rings", 3), 1, 8);
        var inward = string.Equals(context.GetString("direction", "outward"), "inward", StringComparison.Ordinal);
        var time = (float)context.Time * context.Speed;
        var phase = inward ? radius * rings + time : radius * rings - time;
        var t = PaletteHelper.Frac(phase);
        var crest = 0.5f + 0.5f * MathF.Cos((t - 0.15f) * MathF.Tau);
        crest = crest * crest * crest;
        var color = PaletteHelper.SampleGradient(context.Colors, t, wrap: true);
        var core = MathF.Exp(-radius * radius * 6f) * 0.35f;
        return color.Scale(Math.Clamp(0.08f + crest * 0.92f + core, 0f, 1f));
    }
}
