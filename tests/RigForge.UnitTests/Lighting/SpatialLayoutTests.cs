using RigForge.Application.Effects;
using RigForge.Application.Effects.Library;
using RigForge.Application.Layout;
using RigForge.Core.Models;

namespace RigForge.UnitTests.Lighting;

public sealed class SpatialLayoutTests
{
    private static RgbZone Strip(string id, int leds) => new()
    {
        Id = id,
        Name = id,
        Topology = LedTopology.Linear,
        Leds = LedLayouts.Linear(leds),
    };

    [Fact]
    public void Leds_are_mapped_through_the_placement()
    {
        var device = new FakeRgbDevice("dev", Strip("z", 2));
        var placement = new ZonePlacement { DeviceId = "dev", ZoneId = "z", X = 10, Y = 20, Width = 100, Height = 50 };

        var plan = LedSamplePlan.Build(device, new Dictionary<(string, string), ZonePlacement> { [("dev", "z")] = placement }, canvasWidth: 320, canvasHeight: 180);

        Approx.Equal(10f, plan.X[0]);
        Approx.Equal(45f, plan.Y[0]);
        Approx.Equal(110f, plan.X[1]);
        Approx.Equal(45f, plan.Y[1]);
    }

    [Fact]
    public void Rotation_is_clockwise_around_the_top_left_corner()
    {
        var zone = Strip("z", 1) with { Leds = [new LedPosition(1f, 0f)] };
        var device = new FakeRgbDevice("dev", zone);
        var placement = new ZonePlacement { DeviceId = "dev", ZoneId = "z", X = 10, Y = 20, Width = 100, Height = 50, Rotation = 90 };

        var plan = LedSamplePlan.Build(device, new Dictionary<(string, string), ZonePlacement> { [("dev", "z")] = placement }, canvasWidth: 320, canvasHeight: 180);

        Approx.Equal(10f, plan.X[0]);
        Approx.Equal(120f, plan.Y[0]);
    }

    [Fact]
    public void Unplaced_and_disabled_zones_stay_dark()
    {
        var device = new FakeRgbDevice("dev", Strip("on", 1), Strip("off", 1), Strip("unplaced", 1));
        var placements = new Dictionary<(string, string), ZonePlacement>
        {
            [("dev", "on")] = new() { DeviceId = "dev", ZoneId = "on", Width = 4, Height = 4 },
            [("dev", "off")] = new() { DeviceId = "dev", ZoneId = "off", Width = 4, Height = 4, Enabled = false },
        };
        var white = Enumerable.Repeat((byte)255, 8 * 8 * 4).ToArray();

        var plan = LedSamplePlan.Build(device, placements, canvasWidth: 8, canvasHeight: 8);
        plan.Sample(white, 8, 8, brightness: 1f);

        Assert.Equal(RgbColor.White, plan.Colors[0]);
        Assert.Equal(RgbColor.Black, plan.Colors[1]);
        Assert.Equal(RgbColor.Black, plan.Colors[2]);
    }

    [Fact]
    public void Bilinear_sampling_blends_neighbouring_pixels()
    {
        byte[] pixels = [0, 0, 0, 255, 255, 255, 255, 255];

        var color = LedSamplePlan.SampleBilinear(pixels, width: 2, height: 1, x: 1f, y: 0.5f);

        Assert.InRange(color.R, 127, 129);
    }

    [Fact]
    public void Precomputed_taps_match_bilinear_sampling()
    {
        var zone = Strip("z", 1) with { Leds = [new LedPosition(0.5f, 0.5f)] };
        var device = new FakeRgbDevice("dev", zone);
        var placements = new Dictionary<(string, string), ZonePlacement>
        {
            [("dev", "z")] = new() { DeviceId = "dev", ZoneId = "z", X = 0, Y = 0, Width = 2, Height = 1, Enabled = true },
        };
        byte[] pixels = [0, 0, 0, 255, 255, 128, 64, 255];

        var plan = LedSamplePlan.Build(device, placements, canvasWidth: 2, canvasHeight: 1);
        plan.Sample(pixels, 2, 1, brightness: 1f);

        Assert.InRange(plan.Colors[0].R, 127, 129);
        Assert.InRange(plan.Colors[0].G, 63, 65);
        Assert.InRange(plan.Colors[0].B, 31, 33);
    }

    [Fact]
    public void Precomputed_tap_weights_never_overflow_pure_white_at_rounding_boundary()
    {
        var zone = Strip("z", 1) with { Leds = [new LedPosition(0f, 0f)] };
        var device = new FakeRgbDevice("dev", zone);
        var placements = new Dictionary<(string, string), ZonePlacement>
        {
            [("dev", "z")] = new()
            {
                DeviceId = "dev",
                ZoneId = "z",
                X = 1.0f,
                Y = 0.5f + 1f / 256f,
                Width = 2,
                Height = 2,
                Enabled = true,
            },
        };
        var white = Enumerable.Repeat((byte)255, 4 * 4 * 4).ToArray();

        var plan = LedSamplePlan.Build(device, placements, canvasWidth: 4, canvasHeight: 4);
        plan.Sample(white, 4, 4, brightness: 1f);

        Assert.Equal(256, plan.Taps[0].W00 + plan.Taps[0].W10 + plan.Taps[0].W01 + plan.Taps[0].W11);
        Assert.Equal(RgbColor.White, plan.Colors[0]);
    }

    [Fact]
    public void Pure_csharp_effects_render_into_rgba_span_without_skia()
    {
        var rgba = new byte[16 * 8 * 4];
        var settings = EffectSettingsFactory.Create(
            "gradient",
            ("colors", new[] { "#FF0040", "#00FF80" }),
            ("speed", 1f),
            ("angle", 45f));
        var ctx = new EffectFrameContext(
            16, 8, Time: 0.5, settings, [new RgbColor(255, 0, 64), new RgbColor(0, 255, 128)], TelemetrySnapshot.Empty,
            Speed: 1f, Angle: 45f);

        using var solid = new SolidColorEffect();
        solid.Render(rgba, in ctx);
        Assert.Equal([255, 0, 64, 255], rgba[..4]);

        using var wave = new RainbowWaveEffect();
        wave.Render(rgba, in ctx);
        Assert.Equal(255, rgba[3]);
    }

    [Fact]
    public void Unplaced_zones_are_auto_arranged_inside_the_canvas()
    {
        var devices = new[] { new FakeRgbDevice("a", Strip("z1", 10), Strip("z2", 10)), new FakeRgbDevice("b", Strip("z", 5)) };
        var canvas = new VirtualCanvas
        {
            Width = 320,
            Height = 180,
            Placements = [new ZonePlacement { DeviceId = "a", ZoneId = "z1", X = 1, Y = 2 }],
        };

        var effective = CanvasLayoutService.BuildEffectiveCanvas(canvas, devices);

        Assert.Equal(3, effective.Placements.Count);
        Assert.Same(canvas.Placements[0], effective.Placements[0]);
        Assert.All(effective.Placements.Skip(1), p =>
        {
            Assert.InRange(p.X, 0f, canvas.Width - p.Width);
            Assert.InRange(p.Y, 0f, canvas.Height - p.Height);
        });
    }
}
