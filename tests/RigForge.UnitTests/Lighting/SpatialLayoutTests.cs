using Microsoft.Extensions.Logging.Abstractions;
using RigForge.Application.Devices;
using RigForge.Application.Effects;
using RigForge.Application.Effects.Library;
using RigForge.Application.Layout;
using RigForge.Application.Profiles;
using RigForge.Application.Validation;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Platform;

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

    [Fact]
    public void Matrix_layout_is_row_major_and_serpentine_reverses_odd_rows()
    {
        var straight = LedLayouts.Matrix2D(6, 3);
        Assert.Equal(new LedPosition(0.5f / 3f, 0.5f / 2f), straight[0]);
        Assert.Equal(new LedPosition(2.5f / 3f, 0.5f / 2f), straight[2]);
        Assert.Equal(new LedPosition(0.5f / 3f, 1.5f / 2f), straight[3]);

        var snake = LedLayouts.Matrix2D(6, 3, serpentine: true);
        Assert.Equal(straight[0], snake[0]);
        Assert.Equal(new LedPosition(2.5f / 3f, 1.5f / 2f), snake[3]);
        Assert.Equal(new LedPosition(0.5f / 3f, 1.5f / 2f), snake[5]);
    }

    [Fact]
    public void Segments_of_one_zone_are_placed_in_chain_order()
    {
        var device = new FakeRgbDevice("dev", new RgbZone
        {
            Id = "argb1",
            Name = "ARGB",
            Topology = LedTopology.Linear,
            Leds = LedLayouts.Linear(6),
            IsResizable = true,
            MaxLedCount = 40,
        });
        ZonePlacement[] placements =
        [
            new()
            {
                DeviceId = "dev",
                ZoneId = "argb1",
                SegmentId = "block",
                X = 0,
                Y = 0,
                Width = 30,
                Height = 10,
                LedCount = 2,
                Topology = LedTopology.Linear,
            },
            new()
            {
                DeviceId = "dev",
                ZoneId = "argb1",
                SegmentId = "fan",
                X = 100,
                Y = 40,
                Width = 40,
                Height = 40,
                LedCount = 4,
                Topology = LedTopology.Matrix2D,
                MatrixColumns = 2,
            },
        ];

        var plan = LedSamplePlan.Build(device, placements, canvasWidth: 320, canvasHeight: 180);

        Assert.Equal(6, plan.Points.Length);
        Approx.Equal(0f, plan.X[0]);
        Approx.Equal(5f, plan.Y[0]);
        Approx.Equal(30f, plan.X[1]);
        Approx.Equal(110f, plan.X[2]);
        Approx.Equal(50f, plan.Y[2]);
        Approx.Equal(130f, plan.X[3]);
        Approx.Equal(110f, plan.X[4]);
        Approx.Equal(70f, plan.Y[4]);
        Assert.All(plan.Points, point => Assert.True(point.Enabled));
    }

    [Fact]
    public void Leds_past_the_segment_chain_stay_disabled()
    {
        var device = new FakeRgbDevice("dev", Strip("argb1", 4));
        ZonePlacement[] placements =
        [
            new() { DeviceId = "dev", ZoneId = "argb1", SegmentId = "a", Width = 10, Height = 10, LedCount = 1, Topology = LedTopology.Linear },
            new() { DeviceId = "dev", ZoneId = "argb1", SegmentId = "b", X = 20, Width = 10, Height = 10, LedCount = 1, Topology = LedTopology.Linear },
        ];

        var plan = LedSamplePlan.Build(device, placements, canvasWidth: 320, canvasHeight: 180);

        Assert.True(plan.Points[0].Enabled);
        Assert.True(plan.Points[1].Enabled);
        Assert.False(plan.Points[2].Enabled);
        Assert.False(plan.Points[3].Enabled);
    }

    [Fact]
    public void Segment_that_does_not_fit_keeps_the_prefix_of_its_full_layout()
    {
        var device = new FakeRgbDevice("dev", Strip("argb1", 6), Strip("next", 2));
        ZonePlacement[] placements =
        [
            new()
            {
                DeviceId = "dev",
                ZoneId = "argb1",
                SegmentId = "a",
                X = 0,
                Y = 0,
                Width = 90,
                Height = 10,
                LedCount = 4,
                Topology = LedTopology.Linear,
            },
            new()
            {
                DeviceId = "dev",
                ZoneId = "argb1",
                SegmentId = "b",
                X = 100,
                Y = 0,
                Width = 90,
                Height = 10,
                LedCount = 4,
                Topology = LedTopology.Linear,
            },
            new() { DeviceId = "dev", ZoneId = "next", X = 50, Y = 4, Width = 30, Height = 10 },
        ];

        var plan = LedSamplePlan.Build(device, placements, canvasWidth: 320, canvasHeight: 180);

        Assert.Equal(8, plan.Points.Length);
        Approx.Equal(0f, plan.X[0]);
        Approx.Equal(30f, plan.X[1]);
        Approx.Equal(60f, plan.X[2]);
        Approx.Equal(90f, plan.X[3]);
        // First two LEDs of a 4-LED strip at x = 100, not the ends of a regenerated 2-LED strip (100 and 190).
        Approx.Equal(100f, plan.X[4]);
        Approx.Equal(130f, plan.X[5]);
        Approx.Equal(5f, plan.Y[4]);
        Approx.Equal(5f, plan.Y[5]);
        Assert.All(plan.Points.Take(6), point => Assert.True(point.Enabled));

        Approx.Equal(50f, plan.X[6]);
        Approx.Equal(9f, plan.Y[6]);
        Approx.Equal(80f, plan.X[7]);
        Approx.Equal(9f, plan.Y[7]);
        Assert.True(plan.Points[6].Enabled);
        Assert.True(plan.Points[7].Enabled);
    }

    [Fact]
    public void Matrix_that_does_not_fit_keeps_the_full_grid_rows()
    {
        var device = new FakeRgbDevice("dev", Strip("argb1", 3));
        ZonePlacement[] placements =
        [
            new()
            {
                DeviceId = "dev",
                ZoneId = "argb1",
                X = 0,
                Y = 0,
                Width = 100,
                Height = 90,
                LedCount = 6,
                Topology = LedTopology.Matrix2D,
                MatrixColumns = 2,
            },
        ];

        var plan = LedSamplePlan.Build(device, placements, canvasWidth: 320, canvasHeight: 180);

        // 6 LEDs, 2 columns, 3 rows: first-row Y is 0.5/3 of the box (15), not 0.5/2 (22.5) from a 3-LED matrix.
        Assert.Equal(3, plan.Points.Length);
        Approx.Equal(25f, plan.X[0]);
        Approx.Equal(15f, plan.Y[0]);
        Approx.Equal(75f, plan.X[1]);
        Approx.Equal(15f, plan.Y[1]);
        Approx.Equal(25f, plan.X[2]);
        Approx.Equal(45f, plan.Y[2]);
        Assert.All(plan.Points, point => Assert.True(point.Enabled));
    }

    [Fact]
    public void Short_segment_chain_leaves_the_tail_disabled_without_shifting_the_next_zone()
    {
        var device = new FakeRgbDevice("dev", Strip("argb1", 4), Strip("next", 2));
        ZonePlacement[] placements =
        [
            new() { DeviceId = "dev", ZoneId = "argb1", SegmentId = "a", Width = 10, Height = 10, LedCount = 1, Topology = LedTopology.Linear },
            new() { DeviceId = "dev", ZoneId = "argb1", SegmentId = "b", X = 20, Width = 10, Height = 10, LedCount = 1, Topology = LedTopology.Linear },
            new() { DeviceId = "dev", ZoneId = "next", X = 50, Y = 4, Width = 30, Height = 10 },
        ];

        var plan = LedSamplePlan.Build(device, placements, canvasWidth: 320, canvasHeight: 180);

        Assert.True(plan.Points[0].Enabled);
        Assert.True(plan.Points[1].Enabled);
        Assert.False(plan.Points[2].Enabled);
        Assert.False(plan.Points[3].Enabled);
        Approx.Equal(50f, plan.X[4]);
        Approx.Equal(9f, plan.Y[4]);
        Approx.Equal(80f, plan.X[5]);
        Approx.Equal(9f, plan.Y[5]);
    }

    [Fact]
    public void One_placement_of_a_zone_suppresses_its_automatic_box()
    {
        var devices = new[] { new FakeRgbDevice("a", Strip("z1", 10), Strip("z2", 10)) };
        var existing = new ZonePlacement
        {
            DeviceId = "a",
            ZoneId = "z1",
            SegmentId = "block",
            X = 1,
            Y = 2,
            Width = 40,
            Height = 10,
            LedCount = 4,
        };
        var canvas = new VirtualCanvas { Width = 320, Height = 180, Placements = [existing] };

        var effective = CanvasLayoutService.BuildEffectiveCanvas(canvas, devices);

        Assert.Equal(2, effective.Placements.Count);
        Assert.Same(existing, effective.Placements[0]);
        Assert.Equal("z2", effective.Placements[1].ZoneId);
        Assert.Equal("a", effective.Placements[1].DeviceId);
    }

    [Fact]
    public async Task Segment_led_counts_resize_the_physical_zone()
    {
        var device = new FakeRgbDevice("dev", new RgbZone
        {
            Id = "argb1",
            Name = "ARGB",
            Topology = LedTopology.Linear,
            Leds = LedLayouts.Linear(4),
            IsResizable = true,
            MaxLedCount = 40,
        });
        var devices = new DeviceManager([new StaticProbe(device)], NullLogger<DeviceManager>.Instance);
        await devices.ScanAsync(TestContext.Current.CancellationToken);
        var profiles = new ProfileService(
            new InMemoryProfileStore(),
            new RigProfileValidator(new EffectRegistry(new NullScreenCapturer())),
            NullLogger<ProfileService>.Instance);
        var result = await profiles.UpdateCanvasAsync(
            new VirtualCanvas
            {
                Placements =
                [
                    new ZonePlacement { DeviceId = "dev", ZoneId = "argb1", SegmentId = "a", Width = 10, Height = 10, LedCount = 10 },
                    new ZonePlacement { DeviceId = "dev", ZoneId = "argb1", SegmentId = "b", X = 20, Width = 10, Height = 10, LedCount = 12 },
                ],
            },
            TestContext.Current.CancellationToken);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));

        new CanvasLayoutService(devices, profiles).ApplyZoneSizes();

        Assert.Equal(22, device.Zones[0].LedCount);
    }

    [Fact]
    public void Color_correction_tints_sampled_and_direct_colors()
    {
        var device = new FakeRgbDevice("dev", Strip("z", 1));
        var placements = new Dictionary<(string, string), ZonePlacement>
        {
            [("dev", "z")] = new() { DeviceId = "dev", ZoneId = "z", Width = 4, Height = 4 },
        };
        var correction = new DeviceColorCorrection { DeviceId = "dev", Gamma = 2f, WhiteBalance = "#FF8080" };
        var plan = LedSamplePlan.Build(device, placements, canvasWidth: 8, canvasHeight: 8, correction);
        var white = Enumerable.Repeat((byte)255, 8 * 8 * 4).ToArray();

        plan.Sample(white, 8, 8, brightness: 1f);
        var expected = (byte)Math.Clamp(MathF.Pow(1f, 2f) * (128f / 255f) * 255f + 0.5f, 0f, 255f);
        Assert.Equal(new RgbColor(255, expected, expected), plan.Colors[0]);

        plan.Colors[0] = new RgbColor(128, 128, 128);
        plan.ApplyDirectBrightness(1f);
        var half = (byte)Math.Clamp(MathF.Pow(128f / 255f, 2f) * 255f + 0.5f, 0f, 255f);
        var halfTinted = (byte)Math.Clamp(MathF.Pow(128f / 255f, 2f) * (128f / 255f) * 255f + 0.5f, 0f, 255f);
        Assert.Equal(new RgbColor(half, halfTinted, halfTinted), plan.Colors[0]);
    }

    [Fact]
    public void Color_correction_allocates_nothing_after_warmup()
    {
        var device = new FakeRgbDevice("dev", Strip("z", 1));
        var placements = new Dictionary<(string, string), ZonePlacement>
        {
            [("dev", "z")] = new() { DeviceId = "dev", ZoneId = "z", Width = 4, Height = 4 },
        };
        var plan = LedSamplePlan.Build(
            device,
            placements,
            canvasWidth: 8,
            canvasHeight: 8,
            new DeviceColorCorrection { DeviceId = "dev", Gamma = 2.2f, WhiteBalance = "#CCEEFF" });
        plan.Colors[0] = RgbColor.White;
        plan.ApplyDirectBrightness(0.8f);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            plan.Colors[0] = new RgbColor(255, 128, 64);
            plan.ApplyDirectBrightness(0.8f);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }
}
