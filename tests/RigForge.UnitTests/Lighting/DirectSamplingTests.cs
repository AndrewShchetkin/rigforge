using RigForge.Application.Effects;
using RigForge.Application.Effects.Library;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Platform;

namespace RigForge.UnitTests.Lighting;

public sealed class DirectSamplingTests
{
    private static readonly RgbColor[] Palette = [new(255, 0, 64), new(0, 255, 128), new(0, 64, 255)];

    private static EffectFrameContext Context(
        IReadOnlyList<RgbColor>? colors = null,
        double time = 0.5,
        float speed = 1f,
        float angle = 45f,
        TelemetrySnapshot? telemetry = null) =>
        new(
            320, 180, time,
            EffectSettingsFactory.Create("test"),
            colors ?? Palette,
            telemetry ?? TelemetrySnapshot.Empty,
            Speed: speed,
            Angle: angle,
            SensorSelector: "role:CpuTemperature",
            TemperatureLow: 20f,
            TemperatureHigh: 80f);

    private static IEffect CreateEffect(string effectId) => effectId switch
    {
        SolidColorEffect.EffectId => new SolidColorEffect(),
        BreathingEffect.EffectId => new BreathingEffect(),
        SpectrumCycleEffect.EffectId => new SpectrumCycleEffect(),
        RainbowWaveEffect.EffectId => new RainbowWaveEffect(),
        GradientWaveEffect.EffectId => new GradientWaveEffect(),
        TemperatureEffect.EffectId => new TemperatureEffect(),
        _ => throw new ArgumentOutOfRangeException(nameof(effectId), effectId, null),
    };

    private static TelemetrySnapshot TelemetryAt(float value) => new(
        DateTimeOffset.UtcNow,
        [new SensorReading("cpu/temp", "CPU", SensorKind.Temperature, value) { Role = SensorRole.CpuTemperature }]);

    [Theory]
    [InlineData(SolidColorEffect.EffectId)]
    [InlineData(BreathingEffect.EffectId)]
    [InlineData(SpectrumCycleEffect.EffectId)]
    [InlineData(RainbowWaveEffect.EffectId)]
    [InlineData(GradientWaveEffect.EffectId)]
    [InlineData(TemperatureEffect.EffectId)]
    public void Procedural_effects_support_direct_sampling(string effectId)
    {
        using var effect = CreateEffect(effectId);
        var context = Context(telemetry: TelemetryAt(50f));
        LedPoint[] points = [new(0.5f, 0.5f, true), new(100.5f, 90.5f, true), new(319.5f, 179.5f, true)];
        var destination = new RgbColor[points.Length];

        var sampled = effect.TrySamplePoints(points, destination, in context);

        Assert.True(sampled);
        Assert.All(destination, color => Assert.NotEqual(default, color));
    }

    [Theory]
    [InlineData(SolidColorEffect.EffectId)]
    [InlineData(BreathingEffect.EffectId)]
    [InlineData(SpectrumCycleEffect.EffectId)]
    [InlineData(TemperatureEffect.EffectId)]
    public void Uniform_effects_sample_the_same_color_as_render(string effectId)
    {
        using var effect = CreateEffect(effectId);
        var context = Context(telemetry: TelemetryAt(50f));
        var rgba = new byte[320 * 180 * 4];
        effect.Render(rgba, in context);
        var expected = new RgbColor(rgba[0], rgba[1], rgba[2]);

        LedPoint[] points = [new(10.5f, 20.5f, true), new(200.5f, 100.5f, true)];
        var destination = new RgbColor[points.Length];
        Assert.True(effect.TrySamplePoints(points, destination, in context));

        Assert.All(destination, color => Assert.Equal(expected, color));
    }

    [Theory]
    [InlineData(RainbowWaveEffect.EffectId)]
    [InlineData(GradientWaveEffect.EffectId)]
    public void Wave_effects_sample_close_to_the_rasterized_canvas(string effectId)
    {
        using var effect = CreateEffect(effectId);
        var context = Context();
        var rgba = new byte[320 * 180 * 4];
        effect.Render(rgba, in context);

        LedPoint[] points = [new(10.5f, 20.5f, true), new(100.5f, 90.5f, true), new(300.5f, 170.5f, true)];
        var destination = new RgbColor[points.Length];
        Assert.True(effect.TrySamplePoints(points, destination, in context));

        for (var i = 0; i < points.Length; i++)
        {
            var expected = LedSamplePlan.SampleBilinear(rgba, 320, 180, points[i].X, points[i].Y);
            Assert.InRange(Math.Abs(destination[i].R - expected.R), 0, 3);
            Assert.InRange(Math.Abs(destination[i].G - expected.G), 0, 3);
            Assert.InRange(Math.Abs(destination[i].B - expected.B), 0, 3);
        }
    }

    [Fact]
    public void Temperature_effect_without_a_reading_uses_the_first_color()
    {
        using var effect = new TemperatureEffect();
        var context = Context();
        var destination = new RgbColor[2];

        Assert.True(effect.TrySamplePoints([new(0, 0, true), new(10, 10, true)], destination, in context));
        Assert.All(destination, color => Assert.Equal(Palette[0], color));
    }

    [Fact]
    public void Ambilight_declines_direct_sampling()
    {
        using var effect = new AmbilightEffect(new NullScreenCapturer());
        var context = Context();
        var destination = new RgbColor[1];

        Assert.False(effect.TrySamplePoints([new(0, 0, true)], destination, in context));
    }

    [Fact]
    public void Direct_sampling_drives_disabled_leds_black_and_scales_brightness()
    {
        var device = new FakeRgbDevice(
            "dev",
            new RgbZone { Id = "on", Name = "on", Topology = LedTopology.Linear, Leds = LedLayouts.Linear(1) },
            new RgbZone { Id = "off", Name = "off", Topology = LedTopology.Linear, Leds = LedLayouts.Linear(1) });
        var placements = new Dictionary<(string, string), ZonePlacement>
        {
            [("dev", "on")] = new() { DeviceId = "dev", ZoneId = "on", Width = 4, Height = 4 },
            [("dev", "off")] = new() { DeviceId = "dev", ZoneId = "off", Width = 4, Height = 4, Enabled = false },
        };
        var plan = LedSamplePlan.Build(device, placements, canvasWidth: 320, canvasHeight: 180);

        using var effect = new SolidColorEffect();
        var context = Context(colors: [new RgbColor(255, 0, 0)]);
        Assert.True(effect.TrySamplePoints(plan.Points, plan.Colors, in context));
        plan.ApplyDirectBrightness(0.5f);

        Assert.Equal(new RgbColor(128, 0, 0), plan.Colors[0]);
        Assert.Equal(RgbColor.Black, plan.Colors[1]);
    }

    [Fact]
    public void Plan_points_match_led_coordinates_and_enabled_flags()
    {
        var device = new FakeRgbDevice(
            "dev",
            new RgbZone { Id = "z", Name = "z", Topology = LedTopology.Linear, Leds = LedLayouts.Linear(2) });
        var placement = new ZonePlacement { DeviceId = "dev", ZoneId = "z", X = 10, Y = 20, Width = 100, Height = 50 };

        var plan = LedSamplePlan.Build(device, new Dictionary<(string, string), ZonePlacement> { [("dev", "z")] = placement }, canvasWidth: 320, canvasHeight: 180);

        Assert.Equal(plan.X.Length, plan.Points.Length);
        for (var i = 0; i < plan.Points.Length; i++)
        {
            Assert.Equal(plan.X[i], plan.Points[i].X);
            Assert.Equal(plan.Y[i], plan.Points[i].Y);
            Assert.Equal(plan.Enabled[i], plan.Points[i].Enabled);
        }
    }

    [Fact]
    public void Try_sample_points_allocates_nothing_after_warmup()
    {
        IEffect[] effects =
        [
            new SolidColorEffect(),
            new BreathingEffect(),
            new SpectrumCycleEffect(),
            new RainbowWaveEffect(),
            new GradientWaveEffect(),
            new TemperatureEffect(),
        ];
        try
        {
            var context = Context(telemetry: TelemetryAt(50f));
            LedPoint[] points = [new(10.5f, 20.5f, true), new(100.5f, 90.5f, true), new(300.5f, 170.5f, true)];
            var destination = new RgbColor[points.Length];

            // Extended warmup lets tiered JIT promotion finish before the measurement.
            for (var iteration = 0; iteration < 100; iteration++)
            {
                foreach (var effect in effects)
                {
                    effect.TrySamplePoints(points, destination, in context);
                }
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var iteration = 0; iteration < 100; iteration++)
            {
                foreach (var effect in effects)
                {
                    effect.TrySamplePoints(points, destination, in context);
                }
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
        }
        finally
        {
            foreach (var effect in effects)
            {
                effect.Dispose();
            }
        }
    }
}
