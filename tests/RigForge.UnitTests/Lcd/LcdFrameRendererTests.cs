using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RigForge.Application;
using RigForge.Application.AioDisplay;
using RigForge.Application.Devices;
using RigForge.Application.Effects;
using RigForge.Application.Profiles;
using RigForge.Application.Telemetry;
using RigForge.Application.Validation;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Platform;

namespace RigForge.UnitTests.Lcd;

public sealed class LcdFrameRendererTests
{
    private static readonly TelemetrySnapshot Telemetry = new(DateTimeOffset.UtcNow,
    [
        new SensorReading("cpu", "Tctl", SensorKind.Temperature, 62.4f) { Role = SensorRole.CpuTemperature },
        new SensorReading("gpu", "GPU", SensorKind.Temperature, 45f) { Role = SensorRole.GpuTemperature },
        new SensorReading("liquid", "Liquid", SensorKind.Temperature, 31.2f) { Role = SensorRole.LiquidTemperature },
    ]);

    [Theory]
    [InlineData(LcdMode.Dashboard, 240)]
    [InlineData(LcdMode.SingleSensor, 240)]
    [InlineData(LcdMode.Graph, 240)]
    [InlineData(LcdMode.Dashboard, 640)]
    public void Renders_a_full_rgba_frame_with_content(LcdMode mode, int size)
    {
        using var renderer = new LcdFrameRenderer(size, size);
        Assert.False(renderer.HasRenderedFrame);

        var history = Enumerable.Range(0, 60).Select(i => 55f + 8f * MathF.Sin(i / 6f)).ToArray();

        var frame = renderer.Render(new LcdSettings { Mode = mode }, Telemetry, history);

        Assert.True(renderer.HasRenderedFrame);
        Assert.True(frame.Span.SequenceEqual(renderer.LastFrame.Span));
        Assert.Equal(size * size * 4, frame.Length);
        Assert.Contains(frame.ToArray().Where((_, i) => i % 4 != 3), b => b > 200);

        // Set RIGFORGE_LCD_SNAPSHOTS to a folder to eyeball the output.
        if (Environment.GetEnvironmentVariable("RIGFORGE_LCD_SNAPSHOTS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, $"lcd-{mode}-{size}.png"), renderer.EncodePng());
        }
    }

    [Fact]
    public async Task Engine_skips_redundant_renders_and_reuses_last_frame_for_keep_alive()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
        var lcd = new FakeLcdDevice("kraken-lcd", new LcdInfo(240, 240, IsCircular: true, MaxFramesPerSecond: 60f));
        var devices = new DeviceManager([new StaticProbe(lcd)], NullLogger<DeviceManager>.Instance);
        await devices.ScanAsync(TestContext.Current.CancellationToken);

        var profiles = new ProfileService(
            new InMemoryProfileStore(),
            new RigProfileValidator(new EffectRegistry(new NullScreenCapturer())),
            NullLogger<ProfileService>.Instance);
        await profiles.UpdateLcdAsync(new LcdSettings { Mode = LcdMode.Dashboard, FramesPerSecond = 60f }, TestContext.Current.CancellationToken);

        var telemetry = new TelemetryService(devices, Options.Create(new TelemetryOptions()), time, NullLogger<TelemetryService>.Instance);
        await telemetry.PollOnceAsync(TestContext.Current.CancellationToken);

        var options = Options.Create(new LcdOptions { MaxFramesPerSecond = 60f, KeepAliveInterval = TimeSpan.FromSeconds(10) });
        var engine = new LcdRenderEngine(devices, profiles, telemetry, options, time, NullLogger<LcdRenderEngine>.Instance)
        {
            PreviewRequested = true,
        };

        var previews = 0;
        engine.PreviewRendered += _ => Interlocked.Increment(ref previews);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var runTask = engine.RunAsync(cts.Token);

        await lcd.WaitForPushCountAsync(1, TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Unchanged telemetry and settings should not trigger additional renders, device pushes, or PNG encodes.
        Assert.Equal(1, lcd.PushCount);
        Assert.Equal(1, Volatile.Read(ref previews));

        // Advancing past KeepAliveInterval pushes LastFrame without re-encoding preview PNG.
        time.Advance(TimeSpan.FromSeconds(11));
        await lcd.WaitForPushCountAsync(2, TestContext.Current.CancellationToken);

        Assert.Equal(2, lcd.PushCount);
        Assert.Equal(1, Volatile.Read(ref previews));
        Assert.Equal(lcd.FirstFrame, lcd.LastFrame);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
    }

    private sealed class FakeLcdDevice(string id, LcdInfo info) : IAioLcdDevice
    {
        private readonly List<TaskCompletionSource> _waiters = [];
        private readonly Lock _gate = new();

        public string Id => id;

        public string Name => id;

        public string Vendor => "Test";

        public DeviceCapabilities Capabilities => DeviceCapabilities.Lcd;

        public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>();

        public LcdInfo Lcd => info;

        public int PushCount { get; private set; }

        public byte[] FirstFrame { get; private set; } = [];

        public byte[] LastFrame { get; private set; } = [];

        public ValueTask PushFrameAsync(ReadOnlyMemory<byte> rgba, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                var copy = rgba.ToArray();
                if (PushCount == 0)
                {
                    FirstFrame = copy;
                }

                LastFrame = copy;
                PushCount++;
                foreach (var waiter in _waiters)
                {
                    waiter.TrySetResult();
                }
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask SetBrightnessAsync(int percent, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask ShowBuiltInScreenAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public async Task WaitForPushCountAsync(int target, CancellationToken cancellationToken)
        {
            while (true)
            {
                TaskCompletionSource tcs;
                lock (_gate)
                {
                    if (PushCount >= target)
                    {
                        return;
                    }

                    tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _waiters.Add(tcs);
                }

                await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }
}
