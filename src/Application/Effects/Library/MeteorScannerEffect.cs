using RigForge.Core.Models;

namespace RigForge.Application.Effects.Library;

/// <summary>A sweeping beam with a glowing tail: KITT bounce or a continuous meteor.</summary>
internal sealed class MeteorScannerEffect : ProceduralEffect
{
    public const string EffectId = "meteor-scanner";

    private static readonly EffectSelectOption[] Modes =
    [
        new("bounce", "Scanner bounce"),
        new("sweep", "Meteor sweep"),
    ];

    public static readonly EffectDescriptor Info = new(
        EffectId,
        "Meteor scanner",
        "A scanner beam or meteor shower sweeps across with a glowing tail.",
        IsAnimated: true,
        Parameters:
        [
            EffectParameterDefinition.ColorList("colors", "Colors", ["#FF003C", "#FF8A00"], minColors: 1, maxColors: 4),
            EffectParameterDefinition.FloatRange("speed", "Speed", defaultValue: 1.2, min: 0.1, max: 6, step: 0.1, unit: "×"),
            EffectParameterDefinition.FloatRange("angle", "Direction", defaultValue: 0, min: -360, max: 360, step: 1, unit: "°"),
            EffectParameterDefinition.FloatRange("beamWidth", "Beam width", defaultValue: 0.18, min: 0.05, max: 0.5, step: 0.01),
            EffectParameterDefinition.Select("mode", "Mode", "bounce", Modes),
        ],
        Category: EffectCategory.Spatial);

    public override EffectDescriptor Descriptor => Info;

    protected override RgbColor Shade(float u, float v, in EffectFrameContext context)
    {
        var beam = Math.Clamp(context.GetFloat("beamWidth", 0.18f), 0.01f, 1f);
        var time = (float)context.Time * context.Speed;
        var sweep = string.Equals(context.GetString("mode", "bounce"), "sweep", StringComparison.Ordinal);
        var position = PaletteHelper.ProjectUnit(u, v, context.Angle);
        float head;
        float travel;
        if (sweep)
        {
            head = PaletteHelper.Frac(time * 0.35f);
            travel = 1f;
        }
        else
        {
            var cycle = PaletteHelper.Frac(time * 0.5f);
            var goingForward = cycle < 0.5f;
            head = goingForward ? cycle * 2f : 2f - cycle * 2f;
            travel = goingForward ? 1f : -1f;
        }

        var behind = (head - position) * travel;
        var intensity = behind < 0f
            ? MathF.Exp(behind / (beam * 0.22f))
            : MathF.Exp(-behind / beam);
        var colorT = Math.Clamp(1f - MathF.Max(behind, 0f) / (beam * 2.4f), 0f, 1f);
        var color = PaletteHelper.SampleGradient(context.Colors, colorT, wrap: false);
        return color.Scale(MathF.Pow(Math.Clamp(intensity, 0f, 1f), 0.85f));
    }
}
