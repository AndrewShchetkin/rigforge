using RigForge.Core.Models;

namespace RigForge.Application.Effects.Library;

/// <summary>Spectrum bars, a bass shockwave, or a symmetrical VU meter driven by the live loopback bands.</summary>
internal sealed class AudioReactiveEffect : ProceduralEffect
{
    public const string EffectId = "audio-reactive";
    private const int BarCount = 16;

    private static readonly EffectSelectOption[] Styles =
    [
        new("spectrum", "Spectrum bars"),
        new("bass-pulse", "Bass shockwave"),
        new("vu-meter", "Symmetrical VU"),
    ];

    public static readonly EffectDescriptor Info = new(
        EffectId,
        "Audio visualizer",
        "Follows system audio with spectrum bars, a bass shockwave, or a symmetrical VU meter.",
        IsAnimated: true,
        Parameters:
        [
            EffectParameterDefinition.ColorList("colors", "Colors", ["#00F5FF", "#7B2FFF", "#FF0080"], minColors: 2, maxColors: 6),
            EffectParameterDefinition.FloatRange("sensitivity", "Sensitivity", defaultValue: 1.5, min: 0.2, max: 5, step: 0.1),
            EffectParameterDefinition.Select("style", "Style", "spectrum", Styles),
            EffectParameterDefinition.Boolean("idleGlow", "Idle glow", defaultValue: true),
        ],
        Category: EffectCategory.Reactive,
        UsesAudio: true);

    public override EffectDescriptor Descriptor => Info;

    protected override RgbColor Shade(float u, float v, in EffectFrameContext context)
    {
        var idle = IdleAmount(in context);
        var style = context.GetString("style", "spectrum");
        if (string.Equals(style, "bass-pulse", StringComparison.Ordinal))
        {
            return ShadeBass(u, v, in context, idle);
        }

        if (string.Equals(style, "vu-meter", StringComparison.Ordinal))
        {
            return ShadeVu(u, v, in context, idle);
        }

        return ShadeSpectrum(u, v, in context, idle);
    }

    /// <summary>About 12% brightness, breathing slowly, only while the mix is near silent.</summary>
    private static float IdleAmount(in EffectFrameContext context)
    {
        if (context.AudioLevel >= 0.02f || !context.GetBool("idleGlow", true))
        {
            return 0f;
        }

        var wave = 0.5f + 0.5f * MathF.Sin((float)context.Time * 1.6f);
        return 0.12f * (0.65f + 0.35f * wave);
    }

    private static RgbColor ShadeSpectrum(float u, float v, in EffectFrameContext context, float idle)
    {
        var sensitivity = MathF.Max(context.GetFloat("sensitivity", 1.5f), 0f);
        var x = Math.Clamp(u, 0f, 0.9999f) * BarCount;
        var column = (int)x;
        var within = x - column;
        if (within < 0.08f || within > 0.92f)
        {
            return RgbColor.Black;
        }

        var energy = Math.Clamp(context.Band(column) * sensitivity, 0f, 1f);
        var active = context.AudioLevel >= 0.02f;
        var height = active ? energy : idle > 0f ? 0.42f : 0f;
        var gain = active ? 1f : idle;
        var fromBottom = 1f - Math.Clamp(v, 0f, 1f);
        if (height <= 0.0001f || fromBottom > height)
        {
            return RgbColor.Black;
        }

        var color = PaletteHelper.SampleGradient(context.Colors, fromBottom / height, wrap: false);
        var tip = Math.Clamp((fromBottom - (height - 0.06f)) / 0.06f, 0f, 1f);
        return color.Scale(gain * (0.72f + 0.28f * tip));
    }

    private static RgbColor ShadeBass(float u, float v, in EffectFrameContext context, float idle)
    {
        var sensitivity = MathF.Max(context.GetFloat("sensitivity", 1.5f), 0f);
        var bass = Math.Clamp((context.Band(0) + context.Band(1) + context.Band(2)) / 3f * sensitivity, 0f, 1f);
        var dx = u - 0.5f;
        var dy = v - 0.5f;
        var radius = MathF.Sqrt(dx * dx + dy * dy) * 2f;
        var active = context.AudioLevel >= 0.02f && bass > 0.001f;
        float shape;
        float gain;
        if (!active)
        {
            if (idle <= 0f)
            {
                return RgbColor.Black;
            }

            shape = MathF.Exp(-radius * radius * 1.8f);
            gain = idle;
        }
        else
        {
            var ringRadius = 0.18f + bass * 0.95f;
            var delta = radius - ringRadius;
            var ring = MathF.Exp(-(delta * delta) * 72f);
            var core = MathF.Exp(-radius * radius * 4.2f);
            shape = Math.Clamp(core * (0.45f + 0.55f * bass) + ring * bass, 0f, 1f);
            gain = 1f;
        }

        var color = PaletteHelper.SampleGradient(context.Colors, Math.Clamp(radius, 0f, 1f), wrap: false);
        return color.Scale(shape * gain);
    }

    private static RgbColor ShadeVu(float u, float v, in EffectFrameContext context, float idle)
    {
        var sensitivity = MathF.Max(context.GetFloat("sensitivity", 1.5f), 0f);
        var level = Math.Clamp(context.AudioLevel * sensitivity, 0f, 1f);
        var dist = MathF.Abs(u - 0.5f) * 2f;
        var vertical = MathF.Abs(v - 0.5f) * 2f;
        var active = context.AudioLevel >= 0.02f;
        var reach = active ? level : idle > 0f ? 0.7f : 0f;
        var gain = active ? 1f : idle;
        if (reach <= 0f || dist > reach || vertical > 0.72f)
        {
            return RgbColor.Black;
        }

        var color = PaletteHelper.SampleGradient(context.Colors, dist, wrap: false);
        var tip = Math.Clamp(1f - (reach - dist) / 0.05f, 0f, 1f);
        return color.Scale(gain * (0.7f + 0.3f * tip));
    }
}
