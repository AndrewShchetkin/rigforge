using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RigForge.Application;
using RigForge.Application.Devices;
using RigForge.Application.Effects;
using RigForge.Application.Effects.Library;
using RigForge.Application.Profiles;
using RigForge.Application.Telemetry;
using RigForge.Application.Validation;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Platform;
using SkiaSharp;

namespace RigForge.UnitTests.Lighting;

public sealed class CanvasPreviewTests
{
    private readonly RigProfileValidator _validator = new(new EffectRegistry(new NullScreenCapturer()));

    [Fact]
    public async Task Open_preview_publishes_the_rendered_field_and_keeps_exact_led_colors()
    {
        var devices = await DevicesAsync();
        var profiles = new ProfileService(new InMemoryProfileStore(), _validator, NullLogger<ProfileService>.Instance);
        await profiles.UpdateEffectAsync(
            EffectSettingsFactory.Create(SolidColorEffect.EffectId, ("colors", new[] { "#FF0080" })),
            TestContext.Current.CancellationToken);

        var engine = CreateEngine(devices, profiles);
        engine.PreviewRequested = true;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var loop = engine.RunAsync(cts.Token);

        var canvas = await WaitForCanvasAsync(engine, loop, cts.Token);

        Assert.Equal(new byte[] { 255, 0, 128 }, Assert.Single(engine.LatestPreview).Rgb);
        using var bitmap = SKBitmap.Decode(canvas.Jpeg);
        Assert.Equal(320, bitmap.Width);
        Assert.Equal(180, bitmap.Height);
        var pixel = bitmap.GetPixel(160, 90);
        Assert.InRange(pixel.Red, 250, 255);
        Assert.InRange(pixel.Green, 0, 8);
        Assert.InRange(pixel.Blue, 120, 140);

        await cts.CancelAsync();
        await loop;
    }

    [Fact]
    public async Task Open_preview_of_a_wave_is_a_spatial_field()
    {
        var devices = await DevicesAsync();
        var profiles = new ProfileService(new InMemoryProfileStore(), _validator, NullLogger<ProfileService>.Instance);
        var engine = CreateEngine(devices, profiles);
        engine.PreviewRequested = true;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var loop = engine.RunAsync(cts.Token);

        var canvas = await WaitForCanvasAsync(engine, loop, cts.Token);
        using var bitmap = SKBitmap.Decode(canvas.Jpeg);
        var left = bitmap.GetPixel(8, 90);
        var right = bitmap.GetPixel(312, 90);
        var delta = Math.Abs(left.Red - right.Red) + Math.Abs(left.Green - right.Green) + Math.Abs(left.Blue - right.Blue);
        Assert.True(delta > 40, $"Expected a field across the canvas, left {left} right {right}.");

        await cts.CancelAsync();
        await loop;
    }

    [Fact]
    public async Task Closed_preview_renders_frames_without_publishing_a_canvas()
    {
        var devices = await DevicesAsync();
        var profiles = new ProfileService(new InMemoryProfileStore(), _validator, NullLogger<ProfileService>.Instance);
        var engine = CreateEngine(devices, profiles);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var loop = engine.RunAsync(cts.Token);

        while (engine.FramesPerSecond <= 0)
        {
            await Task.Delay(20, cts.Token);
        }

        Assert.Null(engine.LatestCanvas);

        await cts.CancelAsync();
        await loop;
    }

    private static async Task<CanvasFrame> WaitForCanvasAsync(EffectRenderEngine engine, Task loop, CancellationToken cancellationToken)
    {
        while (engine.LatestCanvas is null)
        {
            if (loop.IsFaulted)
            {
                await loop;
            }

            await Task.Delay(20, cancellationToken);
        }

        return engine.LatestCanvas;
    }

    private async Task<DeviceManager> DevicesAsync()
    {
        var rgb = new FakeRgbDevice("led", new RgbZone
        {
            Id = "z",
            Name = "z",
            Topology = LedTopology.Linear,
            Leds = [new LedPosition(0.5f, 0.5f)],
        });
        var devices = new DeviceManager([new StaticProbe(rgb)], NullLogger<DeviceManager>.Instance);
        await devices.ScanAsync(TestContext.Current.CancellationToken);
        return devices;
    }

    private static EffectRenderEngine CreateEngine(DeviceManager devices, ProfileService profiles)
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var telemetry = new TelemetryService(devices, Options.Create(new TelemetryOptions()), time, NullLogger<TelemetryService>.Instance);
        return new EffectRenderEngine(
            devices,
            profiles,
            telemetry,
            new EffectRegistry(new NullScreenCapturer()),
            new SleepPrecisionTimer(),
            Options.Create(new RenderingOptions()),
            NullLogger<EffectRenderEngine>.Instance);
    }
}
