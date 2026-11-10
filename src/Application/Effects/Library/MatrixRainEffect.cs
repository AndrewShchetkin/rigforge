using RigForge.Core.Models;

namespace RigForge.Application.Effects.Library;

/// <summary>Falling code trails. Each column has a hashed speed and phase, with a bright head and an exponential tail.</summary>
internal sealed class MatrixRainEffect : ProceduralEffect
{
    public const string EffectId = "matrix-rain";

    public static readonly EffectDescriptor Info = new(
        EffectId,
        "Digital rain",
        "Glowing code trails fall through vertical columns.",
        IsAnimated: true,
        Parameters:
        [
            EffectParameterDefinition.ColorList("colors", "Colors", ["#00FF41", "#B8FFD0"], minColors: 1, maxColors: 3),
            EffectParameterDefinition.FloatRange("speed", "Speed", defaultValue: 1, min: 0.2, max: 5, step: 0.1, unit: "×"),
            EffectParameterDefinition.IntRange("columns", "Columns", defaultValue: 24, min: 6, max: 64),
            EffectParameterDefinition.FloatRange("tailLength", "Tail length", defaultValue: 0.35, min: 0.1, max: 0.8, step: 0.05),
        ],
        Category: EffectCategory.Spatial);

    public override EffectDescriptor Descriptor => Info;

    protected override RgbColor Shade(float u, float v, in EffectFrameContext context)
    {
        var columns = Math.Clamp(context.GetInt("columns", 24), 1, 256);
        var tail = Math.Clamp(context.GetFloat("tailLength", 0.35f), 0.05f, 1f);
        var time = (float)context.Time * MathF.Max(context.Speed, 0f);
        var column = (int)MathF.Floor(Math.Clamp(u, 0f, 0.999999f) * columns);
        var hash = PaletteHelper.Hash(column, 17, 91);
        var columnSpeed = 0.55f + (hash & 255) * (1f / 255f) * 0.9f;
        var phase = ((hash >> 8) & 1023) * (1f / 1023f);
        var head = PaletteHelper.Frac(phase + time * columnSpeed);
        var behind = head - Math.Clamp(v, 0f, 1f);
        if (behind < 0f)
        {
            behind += 1f;
        }

        var headGlow = MathF.Exp(-(behind * behind) / 0.00008f);
        var trail = MathF.Exp(-behind / tail * 4.2f);
        var brightness = Math.Clamp(headGlow * 0.85f + trail * 0.7f, 0f, 1f);
        var colorT = Math.Clamp(1f - behind / tail, 0f, 1f);
        return PaletteHelper.SampleGradient(context.Colors, colorT, wrap: false).Scale(brightness);
    }
}
