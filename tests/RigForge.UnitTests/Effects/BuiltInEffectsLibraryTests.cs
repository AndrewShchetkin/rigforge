using RigForge.Application.Effects;
using RigForge.Application.Effects.Library;
using RigForge.Application.Validation;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Platform;

namespace RigForge.UnitTests.Effects;

public sealed class BuiltInEffectsLibraryTests
{
    private static readonly RgbColor[] Palette =
    [
        new(0, 255, 157),
        new(0, 200, 255),
        new(123, 47, 255),
        new(255, 0, 128),
    ];

    private static readonly (string Id, string Name, EffectCategory Category, bool Animated, bool Audio)[] Catalog =
    [
        ("rainbow-wave", "Rainbow wave", EffectCategory.Flowing, true, false),
        ("gradient", "Gradient", EffectCategory.Flowing, true, false),
        ("solid", "Solid color", EffectCategory.Static, false, false),
        ("breathing", "Breathing", EffectCategory.Flowing, true, false),
        ("spectrum-cycle", "Spectrum cycle", EffectCategory.Flowing, true, false),
        ("temperature", "Temperature", EffectCategory.Reactive, false, false),
        ("ambilight", "Ambilight", EffectCategory.Reactive, true, false),
        ("aurora", "Aurora borealis", EffectCategory.Flowing, true, false),
        ("spiral-vortex", "Spiral vortex", EffectCategory.Spatial, true, false),
        ("radial-pulse", "Event horizon", EffectCategory.Spatial, true, false),
        ("cyber-wave", "Cyber waves", EffectCategory.Flowing, true, false),
        ("plasma-storm", "Neon plasma", EffectCategory.Spatial, true, false),
        ("fire-lava", "Inferno", EffectCategory.Flowing, true, false),
        ("matrix-rain", "Digital rain", EffectCategory.Spatial, true, false),
        ("starlight", "Starlight", EffectCategory.Flowing, true, false),
        ("meteor-scanner", "Meteor scanner", EffectCategory.Spatial, true, false),
        ("audio-reactive", "Audio visualizer", EffectCategory.Reactive, true, true),
    ];

    private readonly EffectRegistry _registry = new(new NullScreenCapturer());
    private readonly EffectSettingsValidator _validator;

    public BuiltInEffectsLibraryTests()
    {
        _validator = new EffectSettingsValidator(_registry);
    }

    [Fact]
    public void Registry_exposes_17_built_ins_with_valid_defaults()
    {
        Assert.Equal(Catalog.Length, _registry.Descriptors.Count);
        Assert.Equal(17, _registry.Descriptors.Count);

        foreach (var (id, name, category, animated, audio) in Catalog)
        {
            Assert.True(_registry.TryGetDescriptor(id, out var descriptor), id);
            Assert.Equal(id, descriptor.Id);
            Assert.Equal(name, descriptor.Name);
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Description));
            Assert.Equal(category, descriptor.Category);
            Assert.Equal(animated, descriptor.IsAnimated);
            Assert.Equal(audio, descriptor.UsesAudio);

            var settings = new EffectSettings { EffectId = id };
            var result = _validator.Validate(_registry.FillDefaults(settings));
            Assert.True(result.IsValid, id + ": " + string.Join("; ", result.Errors.Select(error => error.ErrorMessage)));
            Assert.True(_validator.Validate(settings).IsValid, id);
        }

        AssertSchema("aurora", "colors:ColorList,speed:FloatRange,scale:FloatRange,turbulence:FloatRange");
        AssertSchema("spiral-vortex", "colors:ColorList,speed:FloatRange,arms:IntRange,twist:FloatRange,clockwise:Boolean");
        AssertSchema("radial-pulse", "colors:ColorList,speed:FloatRange,rings:IntRange,direction:Select");
        AssertSchema("cyber-wave", "colors:ColorList,speed:FloatRange,angle:FloatRange,sharpness:FloatRange");
        AssertSchema("plasma-storm", "colors:ColorList,speed:FloatRange,scale:FloatRange");
        AssertSchema("fire-lava", "colors:ColorList,speed:FloatRange,intensity:FloatRange,cooling:FloatRange");
        AssertSchema("matrix-rain", "colors:ColorList,speed:FloatRange,columns:IntRange,tailLength:FloatRange");
        AssertSchema("starlight", "colors:ColorList,speed:FloatRange,density:FloatRange");
        AssertSchema("meteor-scanner", "colors:ColorList,speed:FloatRange,angle:FloatRange,beamWidth:FloatRange,mode:Select");
        AssertSchema("audio-reactive", "colors:ColorList,sensitivity:FloatRange,style:Select,idleGlow:Boolean");
    }

    [Theory]
    [InlineData("solid")]
    [InlineData("breathing")]
    [InlineData("spectrum-cycle")]
    [InlineData("rainbow-wave")]
    [InlineData("gradient")]
    [InlineData("aurora")]
    [InlineData("spiral-vortex")]
    [InlineData("radial-pulse")]
    [InlineData("cyber-wave")]
    [InlineData("plasma-storm")]
    [InlineData("fire-lava")]
    [InlineData("matrix-rain")]
    [InlineData("starlight")]
    [InlineData("meteor-scanner")]
    [InlineData("audio-reactive")]
    public void Procedural_effects_sample_and_render_without_allocating(string effectId)
    {
        using var effect = _registry.Create(effectId);
        var width = StarlightEffect.CellsX;
        var height = StarlightEffect.CellsY;
        var context = Context(effectId, time: 0.5, width: width, height: height, angle: 30f);
        var rgba = new byte[width * height * 4];
        effect.Render(rgba, in context);

        for (var i = 3; i < rgba.Length; i += 4)
        {
            Assert.Equal(255, rgba[i]);
        }

        Assert.True(ChannelEnergy(rgba) > 0, effectId);

        LedPoint[] points =
        [
            new(0f, 0f, true),
            new(width * 0.25f, height * 0.2f, true),
            new(width * 0.5f, height * 0.5f, true),
            new(width * 0.8f, height * 0.7f, true),
            new(width - 0.5f, height - 0.5f, true),
        ];
        var destination = new RgbColor[points.Length];
        Assert.True(effect.TrySamplePoints(points, destination, in context));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var sampled = effect.TrySamplePoints(points, destination, in context);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(sampled);
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData("aurora")]
    [InlineData("spiral-vortex")]
    [InlineData("radial-pulse")]
    [InlineData("cyber-wave")]
    [InlineData("plasma-storm")]
    [InlineData("fire-lava")]
    [InlineData("matrix-rain")]
    [InlineData("starlight")]
    [InlineData("meteor-scanner")]
    [InlineData("audio-reactive")]
    public void Render_matches_direct_sampling_at_pixel_centers(string effectId)
    {
        const int width = 12;
        const int height = 8;
        using var effect = _registry.Create(effectId);
        var context = Context(effectId, time: 0.85, width: width, height: height, angle: 24f, speed: 1.2f);
        var rgba = new byte[width * height * 4];
        effect.Render(rgba, in context);

        var points = new LedPoint[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                points[y * width + x] = new LedPoint(x + 0.5f, y + 0.5f, true);
            }
        }

        var sampled = new RgbColor[points.Length];
        Assert.True(effect.TrySamplePoints(points, sampled, in context));
        for (var i = 0; i < sampled.Length; i++)
        {
            var offset = i * 4;
            Assert.Equal(sampled[i].R, rgba[offset]);
            Assert.Equal(sampled[i].G, rgba[offset + 1]);
            Assert.Equal(sampled[i].B, rgba[offset + 2]);
            Assert.Equal(255, rgba[offset + 3]);
        }
    }

    [Fact]
    public void Spiral_twist_changes_hue_along_a_ray_and_keeps_a_straight_ray_uniform()
    {
        using var effect = _registry.Create(SpiralVortexEffect.EffectId);
        var near = Point(0.12f, MathF.PI / 3f);
        var far = Point(0.30f, MathF.PI / 3f);
        var straight = Sample(effect, SpiralContext(twist: 0f), near, far);
        var twisted = Sample(effect, SpiralContext(twist: 4f), near, far);

        Assert.True(Energy(straight.Near) > 80, straight.Near.ToString());
        Assert.True(Energy(straight.Far) > 80, straight.Far.ToString());
        Assert.True(SameChroma(straight.Near, straight.Far), $"{straight.Near} vs {straight.Far}");
        Assert.True(Energy(twisted.Near) > 40 && Energy(twisted.Far) > 40);
        Assert.False(SameChroma(twisted.Near, twisted.Far), $"{twisted.Near} vs {twisted.Far}");
    }

    [Fact]
    public void Spiral_direction_and_time_rotate_the_pinwheel()
    {
        using var effect = _registry.Create(SpiralVortexEffect.EffectId);
        var clockwise = At(effect, SpiralContext(twist: 0f, clockwise: true), Point(0.22f, MathF.PI / 6f));
        var counter = At(effect, SpiralContext(twist: 0f, clockwise: false), Point(0.22f, MathF.PI / 6f));
        Assert.True(Energy(clockwise) > 40, clockwise.ToString());
        Assert.True(Energy(counter) > 40, counter.ToString());
        Assert.NotEqual(clockwise, counter);

        var start = Ring(effect, time: 0d);
        var later = Ring(effect, time: 0.37d);
        Assert.True(Differs(start, start[0]), "A vortex arm should not be a flat color.");
        var rotated = false;
        for (var i = 0; i < start.Length; i++)
        {
            if (start[i] != later[i])
            {
                rotated = true;
                break;
            }
        }

        Assert.True(rotated, "Advancing time should rotate the pinwheel.");
    }

    [Fact]
    public void Matrix_rain_heads_fall_and_leave_a_dimmer_tail()
    {
        using var effect = _registry.Create(MatrixRainEffect.EffectId);
        const int columns = 24;
        const int rows = 96;
        var start = Context("matrix-rain", time: 0.2, width: columns, height: rows, speed: 1f, numbers: new Dictionary<string, double> { ["columns"] = columns });
        var later = start with { Time = start.Time + 0.22 };

        var forward = 0;
        for (var column = 0; column < columns; column++)
        {
            var from = BrightestV(effect, in start, column, columns, rows);
            var to = BrightestV(effect, in later, column, columns, rows);
            var delta = to - from;
            if (delta < 0f)
            {
                delta += 1f;
            }

            Assert.InRange(delta, 0.05f, 0.45f);
            forward++;
        }

        Assert.Equal(columns, forward);

        var foundTail = false;
        for (var column = 0; column < columns && !foundTail; column++)
        {
            var head = BrightestV(effect, in start, column, columns, rows);
            if (head is < 0.4f or > 0.7f)
            {
                continue;
            }

            var headColor = At(effect, in start, (column + 0.5f) / columns, head);
            var tailColor = At(effect, in start, (column + 0.5f) / columns, head - 0.16f);
            var belowColor = At(effect, in start, (column + 0.5f) / columns, head + 0.16f);
            Assert.True(Energy(headColor) > Energy(tailColor), $"{headColor} tail {tailColor}");
            Assert.True(Energy(tailColor) > Energy(belowColor), $"{tailColor} below {belowColor}");
            foundTail = true;
        }

        Assert.True(foundTail);

        var across = new RgbColor[columns];
        for (var column = 0; column < columns; column++)
        {
            across[column] = At(effect, in start, (column + 0.5f) / columns, 0.5f);
        }

        Assert.True(Differs(across, across[0]), "Columns should not share one phase.");
    }

    [Fact]
    public void Fire_is_hotter_at_the_base_and_cooling_starves_the_tips()
    {
        using var effect = _registry.Create(FireLavaEffect.EffectId);
        RgbColor[] inferno = [new(0x12, 0x00, 0x00), new(0xB8, 0x14, 0x00), new(0xFF, 0x5E, 0x00), new(0xFF, 0xD0, 0x00)];
        var context = Context("fire-lava", time: 0.4, width: 48, height: 48, speed: 1.2f, palette: inferno);
        var bottom = MeanEnergy(effect, in context, 0.78f, 0.98f);
        var top = MeanEnergy(effect, in context, 0.02f, 0.22f);
        Assert.True(bottom > top + 40, $"bottom {bottom} top {top}");

        var cool = MeanEnergy(effect, Context("fire-lava", time: 0.4, width: 48, height: 48, speed: 1.2f, palette: inferno, numbers: new Dictionary<string, double> { ["cooling"] = 0.2 }), 0.02f, 0.28f);
        var cold = MeanEnergy(effect, Context("fire-lava", time: 0.4, width: 48, height: 48, speed: 1.2f, palette: inferno, numbers: new Dictionary<string, double> { ["cooling"] = 2 }), 0.02f, 0.28f);
        Assert.True(cool > cold + 20, $"low cooling {cool} high cooling {cold}");

        var before = SampleGrid(effect, in context, 16, 16);
        var afterContext = context with { Time = context.Time + 1.3 };
        var after = SampleGrid(effect, in afterContext, 16, 16);
        var delta = 0;
        for (var i = 0; i < before.Length; i++)
        {
            delta += Math.Abs(before[i].R - after[i].R) + Math.Abs(before[i].G - after[i].G) + Math.Abs(before[i].B - after[i].B);
        }

        Assert.True(delta > 200, $"flicker delta {delta}");
    }

    [Theory]
    [InlineData("spectrum")]
    [InlineData("bass-pulse")]
    [InlineData("vu-meter")]
    public void Audio_styles_get_brighter_and_taller_as_the_level_rises(string style)
    {
        using var effect = _registry.Create(AudioReactiveEffect.EffectId);
        var silent = AudioContext(style, level: 0f, bands: Filled(0f), idle: false, sensitivity: 1f);
        var loud = AudioContext(style, level: 1f, bands: Filled(1f), idle: false, sensitivity: 1f);
        var silentEnergy = GridEnergy(effect, in silent);
        var loudEnergy = GridEnergy(effect, in loud);
        Assert.True(loudEnergy > silentEnergy + 500, $"{style} loud {loudEnergy} silent {silentEnergy}");

        if (style == "spectrum")
        {
            var shortBars = AudioContext(style, level: 0.5f, bands: Filled(0.15f), idle: false, sensitivity: 1f);
            var tallBars = AudioContext(style, level: 0.5f, bands: Filled(1f), idle: false, sensitivity: 1f);
            var highQuiet = At(effect, in shortBars, 0.5f / 16f, 0.2f);
            var highLoud = At(effect, in tallBars, 0.5f / 16f, 0.2f);
            Assert.Equal(0, Energy(highQuiet));
            Assert.True(Energy(highLoud) > 80, highLoud.ToString());
        }
        else if (style == "bass-pulse")
        {
            var centerSilent = At(effect, in silent, 0.5f, 0.5f);
            var centerLoud = At(effect, in loud, 0.5f, 0.5f);
            var treble = AudioContext(style, level: 0.8f, bands: Band(15, 1f), idle: false, sensitivity: 1.5f);
            var bass = AudioContext(style, level: 0.8f, bands: Band(0, 1f, 1, 1f, 2, 1f), idle: false, sensitivity: 1.5f);
            Assert.True(Energy(centerLoud) > Energy(centerSilent) + 80, $"{centerLoud} vs {centerSilent}");
            Assert.True(Energy(At(effect, in bass, 0.5f, 0.5f)) > Energy(At(effect, in treble, 0.5f, 0.5f)) + 80);
        }
        else
        {
            var edgeSilent = At(effect, in silent, 0.88f, 0.5f);
            var edgeLoud = At(effect, in loud, 0.88f, 0.5f);
            Assert.Equal(0, Energy(edgeSilent));
            Assert.True(Energy(edgeLoud) > 40, edgeLoud.ToString());
        }
    }

    [Fact]
    public void Audio_reactive_idles_with_a_dim_breath_while_silent()
    {
        using var effect = _registry.Create(AudioReactiveEffect.EffectId);
        var dead = AudioContext("spectrum", level: 0f, bands: Filled(0f), idle: false, sensitivity: 1.5f, time: 0.4);
        var idleEarly = AudioContext("spectrum", level: 0f, bands: Filled(0f), idle: true, sensitivity: 1.5f, time: 0d);
        var idleLate = AudioContext("spectrum", level: 0f, bands: Filled(0f), idle: true, sensitivity: 1.5f, time: 0.9817d);
        var loud = AudioContext("vu-meter", level: 1f, bands: Filled(0f), idle: true, sensitivity: 1.5f, time: 0.4);

        var barU = 0.5f / 16f;
        Assert.Equal(0, Energy(At(effect, in dead, barU, 0.9f)));
        var early = At(effect, in idleEarly, barU, 0.9f);
        var late = At(effect, in idleLate, barU, 0.9f);
        Assert.True(Energy(early) > 0, early.ToString());
        Assert.NotEqual(early, late);
        Assert.True(GridEnergy(effect, in loud) > GridEnergy(effect, in idleLate));
    }

    private void AssertSchema(string effectId, string schema)
    {
        Assert.True(_registry.TryGetDescriptor(effectId, out var descriptor));
        var expected = schema.Split(',');
        Assert.Equal(expected.Length, descriptor.Parameters.Count);
        foreach (var item in expected)
        {
            var parts = item.Split(':');
            var parameter = descriptor.FindParameter(parts[0]);
            Assert.NotNull(parameter);
            Assert.Equal(Enum.Parse<EffectParameterKind>(parts[1]), parameter.Kind);
        }
    }

    private static EffectFrameContext Context(
        string effectId,
        double time,
        int width,
        int height,
        float speed = 1f,
        float angle = 0f,
        float audioLevel = 0f,
        IReadOnlyList<RgbColor>? palette = null,
        ReadOnlyMemory<float> bands = default,
        Dictionary<string, double>? numbers = null,
        Dictionary<string, bool>? flags = null,
        Dictionary<string, string>? strings = null) =>
        new(
            width,
            height,
            time,
            new EffectSettings { EffectId = effectId },
            palette ?? Palette,
            TelemetrySnapshot.Empty,
            Speed: speed,
            Angle: angle,
            AudioLevel: audioLevel,
            AudioBands: bands,
            Numbers: numbers,
            Booleans: flags,
            Strings: strings);

    private static EffectFrameContext SpiralContext(float twist, bool clockwise = true, double time = 0d) =>
        Context(
            SpiralVortexEffect.EffectId,
            time,
            width: 100,
            height: 100,
            speed: 1f,
            numbers: new Dictionary<string, double> { ["twist"] = twist, ["arms"] = 3 },
            flags: new Dictionary<string, bool> { ["clockwise"] = clockwise },
            strings: null);

    private static EffectFrameContext AudioContext(string style, float level, float[] bands, bool idle, float sensitivity, double time = 0.2)
    {
        return Context(
            AudioReactiveEffect.EffectId,
            time,
            width: 48,
            height: 48,
            audioLevel: level,
            bands: bands,
            numbers: new Dictionary<string, double> { ["sensitivity"] = sensitivity },
            flags: new Dictionary<string, bool> { ["idleGlow"] = idle },
            strings: new Dictionary<string, string> { ["style"] = style });
    }

    private static (float U, float V) Point(float radius, float angle) =>
        (0.5f + radius * MathF.Cos(angle), 0.5f + radius * MathF.Sin(angle));

    private static RgbColor At(IEffect effect, in EffectFrameContext context, float u, float v)
    {
        var destination = new RgbColor[1];
        var point = new LedPoint(u * context.Width, v * context.Height, true);
        Assert.True(effect.TrySamplePoints([point], destination, in context));
        return destination[0];
    }

    private static RgbColor At(IEffect effect, in EffectFrameContext context, (float U, float V) point) =>
        At(effect, in context, point.U, point.V);

    private static (RgbColor Near, RgbColor Far) Sample(IEffect effect, in EffectFrameContext context, (float U, float V) near, (float U, float V) far) =>
        (At(effect, in context, near), At(effect, in context, far));

    private static RgbColor[] Ring(IEffect effect, double time)
    {
        var context = SpiralContext(twist: 2f, time: time);
        var colors = new RgbColor[8];
        for (var i = 0; i < colors.Length; i++)
        {
            colors[i] = At(effect, in context, Point(0.28f, i * MathF.Tau / colors.Length));
        }

        return colors;
    }

    private static float BrightestV(IEffect effect, in EffectFrameContext context, int column, int columns, int rows)
    {
        var points = new LedPoint[rows];
        var colors = new RgbColor[rows];
        var u = (column + 0.5f) / columns;
        for (var row = 0; row < rows; row++)
        {
            points[row] = new LedPoint(u * context.Width, (row + 0.5f) / rows * context.Height, true);
        }

        Assert.True(effect.TrySamplePoints(points, colors, in context));
        var best = 0;
        var bestEnergy = -1;
        for (var row = 0; row < rows; row++)
        {
            var energy = Energy(colors[row]);
            if (energy > bestEnergy)
            {
                bestEnergy = energy;
                best = row;
            }
        }

        return (best + 0.5f) / rows;
    }

    private static int MeanEnergy(IEffect effect, in EffectFrameContext context, float v0, float v1)
    {
        const int samples = 12;
        var points = new LedPoint[samples * samples];
        var index = 0;
        for (var y = 0; y < samples; y++)
        {
            var v = v0 + (v1 - v0) * ((y + 0.5f) / samples);
            for (var x = 0; x < samples; x++)
            {
                points[index++] = new LedPoint((x + 0.5f) / samples * context.Width, v * context.Height, true);
            }
        }

        var colors = new RgbColor[points.Length];
        Assert.True(effect.TrySamplePoints(points, colors, in context));
        long sum = 0;
        foreach (var color in colors)
        {
            sum += Energy(color);
        }

        return (int)(sum / colors.Length);
    }

    private static RgbColor[] SampleGrid(IEffect effect, in EffectFrameContext context, int columns, int rows)
    {
        var points = new LedPoint[columns * rows];
        var index = 0;
        for (var y = 0; y < rows; y++)
        {
            var v = (y + 0.5f) / rows;
            for (var x = 0; x < columns; x++)
            {
                points[index++] = new LedPoint((x + 0.5f) / columns * context.Width, v * context.Height, true);
            }
        }

        var colors = new RgbColor[points.Length];
        Assert.True(effect.TrySamplePoints(points, colors, in context));
        return colors;
    }

    private static int GridEnergy(IEffect effect, in EffectFrameContext context)
    {
        var colors = SampleGrid(effect, in context, 24, 24);
        var sum = 0;
        foreach (var color in colors)
        {
            sum += Energy(color);
        }

        return sum;
    }

    private static int ChannelEnergy(ReadOnlySpan<byte> rgba)
    {
        var sum = 0;
        for (var i = 0; i < rgba.Length; i += 4)
        {
            sum += rgba[i] + rgba[i + 1] + rgba[i + 2];
        }

        return sum;
    }

    private static int Energy(RgbColor color) => color.R + color.G + color.B;

    private static bool Differs(ReadOnlySpan<RgbColor> colors, RgbColor reference)
    {
        foreach (var color in colors)
        {
            if (color != reference)
            {
                return true;
            }
        }

        return false;
    }

    private static bool SameChroma(RgbColor left, RgbColor right)
    {
        var leftSum = left.R + left.G + left.B;
        var rightSum = right.R + right.G + right.B;
        if (leftSum == 0 || rightSum == 0)
        {
            return false;
        }

        var dr = left.R / (float)leftSum - right.R / (float)rightSum;
        var dg = left.G / (float)leftSum - right.G / (float)rightSum;
        var db = left.B / (float)leftSum - right.B / (float)rightSum;
        return dr * dr + dg * dg + db * db < 0.0015f;
    }

    private static float[] Filled(float value)
    {
        var bands = new float[16];
        Array.Fill(bands, value);
        return bands;
    }

    private static float[] Band(int index, float value, int index2 = -1, float value2 = 0f, int index3 = -1, float value3 = 0f)
    {
        var bands = new float[16];
        bands[index] = value;
        if ((uint)index2 < 16)
        {
            bands[index2] = value2;
        }

        if ((uint)index3 < 16)
        {
            bands[index3] = value3;
        }

        return bands;
    }
}
