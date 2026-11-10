using RigForge.Core.Models;

namespace RigForge.Application.Effects.Library;

/// <summary>Stars that twinkle in and out. A 48×27 cell grid keeps a 48×27 preview on the cell centers.</summary>
internal sealed class StarlightEffect : ProceduralEffect
{
    public const string EffectId = "starlight";
    public const int CellsX = 48;
    public const int CellsY = 27;

    public static readonly EffectDescriptor Info = new(
        EffectId,
        "Starlight",
        "Stars twinkle in and out across the canvas.",
        IsAnimated: true,
        Parameters:
        [
            EffectParameterDefinition.ColorList("colors", "Colors", ["#FFFFFF", "#7CC8FF", "#FFD280"], minColors: 1, maxColors: 6),
            EffectParameterDefinition.FloatRange("speed", "Speed", defaultValue: 1, min: 0.1, max: 5, step: 0.1, unit: "×"),
            EffectParameterDefinition.FloatRange("density", "Density", defaultValue: 0.4, min: 0.05, max: 1, step: 0.05),
        ],
        Category: EffectCategory.Flowing);

    public override EffectDescriptor Descriptor => Info;

    protected override RgbColor Shade(float u, float v, in EffectFrameContext context)
    {
        var density = Math.Clamp(context.GetFloat("density", 0.4f), 0f, 1f);
        var time = (float)context.Time * MathF.Max(context.Speed, 0f);
        if (time < 0f)
        {
            time = 0f;
        }

        var bucket = (int)MathF.Floor(time);
        var fade = time - bucket;
        var cellX = Cell(u, CellsX);
        var cellY = Cell(v, CellsY);
        var dx = u * CellsX - cellX - 0.5f;
        var dy = v * CellsY - cellY - 0.5f;
        var spark = MathF.Exp(-(dx * dx + dy * dy) * 16f);
        var twinkle = MathF.Sin(fade * MathF.PI);
        if (twinkle <= 0f || spark < 0.004f)
        {
            return RgbColor.Black;
        }

        var hash = PaletteHelper.Hash(cellX, cellY, bucket);
        var chance = (hash & 0xFFFF) * (1f / 65536f);
        if (chance >= density)
        {
            return RgbColor.Black;
        }

        var colorT = ((hash >> 16) & 0xFF) * (1f / 255f);
        return PaletteHelper.SampleGradient(context.Colors, colorT, wrap: false).Scale(Math.Clamp(twinkle * spark, 0f, 1f));
    }

    private static int Cell(float t, int count)
    {
        var cell = (int)MathF.Floor(t * count);
        if (cell < 0)
        {
            return 0;
        }

        return cell >= count ? count - 1 : cell;
    }
}
