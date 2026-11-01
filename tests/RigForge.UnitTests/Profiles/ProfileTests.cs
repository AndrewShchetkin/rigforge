using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RigForge.Application;
using RigForge.Application.AioDisplay;
using RigForge.Application.Devices;
using RigForge.Application.Effects;
using RigForge.Application.Effects.Library;
using RigForge.Application.Profiles;
using RigForge.Application.Telemetry;
using RigForge.Application.Validation;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Host.Daemon.Contracts;
using RigForge.Host.Daemon.Hubs;
using RigForge.Infrastructure.CrossPlatform.Platform;
using RigForge.Infrastructure.Linux.Hardware;

namespace RigForge.UnitTests.Profiles;

public sealed class ProfileTests
{
    private readonly RigProfileValidator _validator = new(new EffectRegistry(new NullScreenCapturer()));

    private static FanCurve Curve(string id) => new() { Id = id, DeviceId = "dev", ChannelId = id };

    [Fact]
    public void Default_profile_is_valid() =>
        Assert.True(_validator.Validate(new RigProfile()).IsValid);

    [Fact]
    public void Unknown_effects_and_out_of_range_values_are_rejected()
    {
        var profile = new RigProfile { Effect = new EffectSettings { EffectId = "nope", Brightness = 2f } };

        var result = _validator.Validate(profile);

        Assert.Contains(result.Errors, e => e.PropertyName == "Effect.EffectId");
        Assert.Contains(result.Errors, e => e.PropertyName == "Effect.Brightness");
    }

    [Fact]
    public void A_channel_cannot_have_two_curves()
    {
        var profile = new RigProfile { FanCurves = [Curve("a"), Curve("b") with { ChannelId = "a" }] };

        Assert.False(_validator.Validate(profile).IsValid);
    }

    [Fact]
    public void Curves_need_distinct_temperatures()
    {
        var profile = new RigProfile { FanCurves = [Curve("a") with { Points = [new(40, 10), new(40, 90)] }] };

        Assert.False(_validator.Validate(profile).IsValid);
    }

    [Fact]
    public void Role_selectors_resolve_case_insensitively_and_deterministically()
    {
        var snapshot = new TelemetrySnapshot(DateTimeOffset.UtcNow,
        [
            new SensorReading("z/cpu", "Z", SensorKind.Temperature, 70) { Role = SensorRole.CpuTemperature },
            new SensorReading("a/cpu", "A", SensorKind.Temperature, 50) { Role = SensorRole.CpuTemperature },
            new SensorReading("z/gpu", "Z GPU", SensorKind.Temperature, 70) { Role = SensorRole.GpuTemperature },
            new SensorReading("a/gpu", "A GPU", SensorKind.Temperature, 70) { Role = SensorRole.GpuTemperature },
        ]);

        Assert.True(snapshot.TryResolve("role:cputemperature", out var cpuReading));
        Assert.Equal("z/cpu", cpuReading.Id);
        Assert.True(snapshot.TryResolve("role:GpuTemperature", out var gpuReading));
        Assert.Equal("a/gpu", gpuReading.Id);
        Assert.True(snapshot.TryResolve("z/cpu", out _));
        Assert.False(snapshot.TryResolve("role:LiquidTemperature", out _));
    }

    [Fact]
    public async Task Corrupt_profile_section_is_backed_up_and_valid_fan_curves_are_salvaged()
    {
        var validCurve = Curve("fan1");
        var store = new InMemoryProfileStore
        {
            Saved = new RigProfile
            {
                Effect = new EffectSettings { EffectId = "nope" },
                FanCurves = [validCurve],
            },
        };
        var service = new ProfileService(store, _validator, NullLogger<ProfileService>.Instance);

        await service.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, store.BackupCount);
        Assert.Equal(new EffectSettings().EffectId, service.Current.Effect.EffectId);
        Assert.Equal(validCurve.Id, Assert.Single(service.Current.FanCurves).Id);
    }

    [Fact]
    public async Task Backup_and_save_io_errors_during_profile_recovery_do_not_throw()
    {
        var validCurve = Curve("fan1");
        var invalidStore = new ThrowingRecoveryProfileStore
        {
            Loaded = new RigProfile
            {
                Effect = new EffectSettings { EffectId = "nope" },
                FanCurves = [validCurve],
            },
        };
        var service = new ProfileService(invalidStore, _validator, NullLogger<ProfileService>.Instance);

        await service.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(validCurve.Id, Assert.Single(service.Current.FanCurves).Id);

        var corruptJsonStore = new ThrowingRecoveryProfileStore { ThrowJsonOnLoad = true };
        var corruptService = new ProfileService(corruptJsonStore, _validator, NullLogger<ProfileService>.Instance);

        await corruptService.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new EffectSettings().EffectId, corruptService.Current.Effect.EffectId);
    }

    [Fact]
    public void Client_registry_ignores_set_preview_after_disconnect_and_signals_changes()
    {
        var devices = new DeviceManager([], NullLogger<DeviceManager>.Instance);
        var profiles = new ProfileService(new InMemoryProfileStore(), _validator, NullLogger<ProfileService>.Instance);
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var telemetry = new TelemetryService(devices, Options.Create(new TelemetryOptions()), time, NullLogger<TelemetryService>.Instance);
        var effects = new EffectRenderEngine(
            devices,
            profiles,
            telemetry,
            new EffectRegistry(new NullScreenCapturer()),
            new SleepPrecisionTimer(),
            Options.Create(new RenderingOptions()),
            NullLogger<EffectRenderEngine>.Instance);
        var lcd = new LcdRenderEngine(
            devices,
            profiles,
            telemetry,
            Options.Create(new LcdOptions()),
            time,
            NullLogger<LcdRenderEngine>.Instance);
        var registry = new ClientRegistry(effects, lcd);
        var signalCount = 0;
        registry.SubscriptionsChanged += () => signalCount++;

        registry.SetPreview("ghost", PreviewChannel.Leds, enabled: true);

        Assert.Equal(0, registry.ClientCount);
        Assert.False(registry.HasSubscribers(PreviewChannel.Leds));
        Assert.False(effects.PreviewRequested);
        Assert.Equal(0, signalCount);

        registry.Connected("c1");
        registry.SetPreview("c1", PreviewChannel.Leds, enabled: true);
        Assert.True(registry.HasSubscribers(PreviewChannel.Leds));
        Assert.True(effects.PreviewRequested);
        Assert.Equal(1, signalCount);

        registry.Disconnected("c1");
        Assert.Equal(0, registry.ClientCount);
        Assert.False(registry.HasSubscribers(PreviewChannel.Leds));
        Assert.False(effects.PreviewRequested);
        Assert.Equal(2, signalCount);
    }

    [Fact]
    public async Task Canceled_telemetry_poll_does_not_leak_the_provider_gate()
    {
        var provider = new FakeFanController("kraken");
        provider.Readings.Add(new SensorReading("kraken/liquid", "Liquid", SensorKind.Temperature, 32f));
        var devices = new DeviceManager([new StaticProbe(provider)], NullLogger<DeviceManager>.Instance);
        await devices.ScanAsync(TestContext.Current.CancellationToken);

        var telemetry = new TelemetryService(
            devices,
            Options.Create(new TelemetryOptions()),
            new ManualTimeProvider(DateTimeOffset.UtcNow),
            NullLogger<TelemetryService>.Instance);

        using var canceledCts = new CancellationTokenSource();
        await canceledCts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => telemetry.PollOnceAsync(canceledCts.Token));

        await telemetry.PollOnceAsync(TestContext.Current.CancellationToken);
        Assert.Single(telemetry.Latest.Readings);
    }

    [Fact]
    public async Task Static_sensor_effect_wakes_on_telemetry_updates()
    {
        var sensor = new FakeFanController("cpu");
        sensor.Readings.Add(new SensorReading("cpu/temp", "CPU", SensorKind.Temperature, 20f) { Role = SensorRole.CpuTemperature });
        var rgb = new FakeRgbDevice("led", new RgbZone
        {
            Id = "z",
            Name = "z",
            Topology = LedTopology.Linear,
            Leds = [new LedPosition(0.5f, 0.5f)],
        });
        var devices = new DeviceManager([new StaticProbe(sensor, rgb)], NullLogger<DeviceManager>.Instance);
        await devices.ScanAsync(TestContext.Current.CancellationToken);

        var profiles = new ProfileService(new InMemoryProfileStore(), _validator, NullLogger<ProfileService>.Instance);
        await profiles.UpdateEffectAsync(
            new EffectSettings
            {
                EffectId = TemperatureEffect.EffectId,
                Colors = ["#0000FF", "#FF0000"],
                TemperatureLow = 20f,
                TemperatureHigh = 80f,
            },
            TestContext.Current.CancellationToken);

        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var telemetry = new TelemetryService(devices, Options.Create(new TelemetryOptions()), time, NullLogger<TelemetryService>.Instance);
        await telemetry.PollOnceAsync(TestContext.Current.CancellationToken);

        var engine = new EffectRenderEngine(
            devices,
            profiles,
            telemetry,
            new EffectRegistry(new NullScreenCapturer()),
            new SleepPrecisionTimer(),
            Options.Create(new RenderingOptions()),
            NullLogger<EffectRenderEngine>.Instance)
        {
            PreviewRequested = true,
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var loop = engine.RunAsync(cts.Token);

        while (engine.LatestPreview.Count == 0)
        {
            await Task.Delay(10, cts.Token);
        }

        Assert.Equal(new byte[] { 0, 0, 255 }, engine.LatestPreview[0].Rgb);

        sensor.Readings[0] = new SensorReading("cpu/temp", "CPU", SensorKind.Temperature, 80f) { Role = SensorRole.CpuTemperature };
        time.Advance(TimeSpan.FromSeconds(1));
        await telemetry.PollOnceAsync(cts.Token);

        while (engine.LatestPreview[0].Rgb[0] != 255)
        {
            await Task.Delay(10, cts.Token);
        }

        Assert.Equal(new byte[] { 255, 0, 0 }, engine.LatestPreview[0].Rgb);

        await cts.CancelAsync();
        await loop;
    }

    [Fact]
    public async Task Hung_sensor_provider_is_not_reentered_on_subsequent_polls()
    {
        var provider = new HungSensorProvider();
        var devices = new DeviceManager([new StaticProbe(provider)], NullLogger<DeviceManager>.Instance);
        await devices.ScanAsync(TestContext.Current.CancellationToken);

        var options = Options.Create(new TelemetryOptions { ProviderTimeout = TimeSpan.FromMilliseconds(20) });
        var telemetry = new TelemetryService(devices, options, new ManualTimeProvider(DateTimeOffset.UtcNow), NullLogger<TelemetryService>.Instance);

        await telemetry.PollOnceAsync(TestContext.Current.CancellationToken);
        await provider.Entered;
        await telemetry.PollOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, provider.MaxConcurrent);
        Assert.Equal(1, provider.Invocations);

        provider.Release();
    }

    [Fact]
    public void Proc_stat_cpu_line_is_parsed()
    {
        var parsed = LinuxSystemDevice.ParseCpuLine("cpu  300 0 200 400 50 30 20 0 0 0");

        Assert.NotNull(parsed);
        Assert.Equal(450UL, parsed.Value.Idle);
        Assert.Equal(1000UL, parsed.Value.Total);
    }

    [Fact]
    public async Task Delta_device_scan_preserves_existing_devices_and_only_disposes_removed_ones()
    {
        var devA = new TrackedDevice("dev-a");
        var devB = new TrackedDevice("dev-b");
        var devC = new TrackedDevice("dev-c");
        var probe = new IncrementalTestProbe([devA, devB], [devA, devC]);
        var manager = new DeviceManager([probe], NullLogger<DeviceManager>.Instance);

        await manager.ScanAsync(TestContext.Current.CancellationToken);

        Assert.Equal([devA, devB], manager.Devices);
        Assert.Equal(0, devA.DisposeCount);
        Assert.Equal(0, devB.DisposeCount);

        await manager.ScanAsync(TestContext.Current.CancellationToken);

        Assert.Equal([devA, devC], manager.Devices);
        Assert.Same(devA, manager.Find("dev-a"));
        Assert.Same(devC, manager.Find("dev-c"));
        Assert.Null(manager.Find("dev-b"));
        Assert.Equal(0, devA.DisposeCount);
        Assert.Equal(1, devB.DisposeCount);
        Assert.Equal(0, devC.DisposeCount);
        Assert.Equal([devA, devB], probe.LastExistingReceived);
    }

    private sealed class TrackedDevice(string id) : IHardwareDevice
    {
        public string Id => id;

        public string Name => id;

        public string Vendor => "Test";

        public DeviceCapabilities Capabilities => DeviceCapabilities.None;

        public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>();

        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class IncrementalTestProbe(
        IReadOnlyList<IHardwareDevice> firstScan,
        IReadOnlyList<IHardwareDevice> secondScan) : IDeviceProbe
    {
        private int _scanCount;

        public string Name => "incremental-test";

        public IReadOnlyList<IHardwareDevice> LastExistingReceived { get; private set; } = [];

        public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken) =>
            ProbeAsync([], cancellationToken);

        public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(
            IReadOnlyList<IHardwareDevice> existingDevices,
            CancellationToken cancellationToken)
        {
            LastExistingReceived = existingDevices;
            _scanCount++;
            return Task.FromResult(_scanCount == 1 ? firstScan : secondScan);
        }
    }

    private sealed class HungSensorProvider : ISensorProvider
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        private int _maxConcurrent;
        private int _invocations;

        public string Id => "hung";

        public string Name => "Hung";

        public string Vendor => "Test";

        public DeviceCapabilities Capabilities => DeviceCapabilities.Sensors;

        public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>();

        public Task Entered => _entered.Task;

        public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);

        public int Invocations => Volatile.Read(ref _invocations);

        public void Release() => _release.TrySetResult();

        public async ValueTask<IReadOnlyList<SensorReading>> ReadSensorsAsync(CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _active);
            Interlocked.Increment(ref _invocations);
            while (true)
            {
                var observed = Volatile.Read(ref _maxConcurrent);
                if (current <= observed || Interlocked.CompareExchange(ref _maxConcurrent, current, observed) == observed)
                {
                    break;
                }
            }

            _entered.TrySetResult();
            try
            {
                await _release.Task;
                return [];
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingRecoveryProfileStore : IProfileStore
    {
        public RigProfile? Loaded { get; init; }

        public bool ThrowJsonOnLoad { get; init; }

        public Task<RigProfile?> LoadAsync(CancellationToken cancellationToken) =>
            ThrowJsonOnLoad
                ? Task.FromException<RigProfile?>(new JsonException("Corrupt JSON"))
                : Task.FromResult(Loaded);

        public Task SaveAsync(RigProfile profile, CancellationToken cancellationToken) =>
            Task.FromException(new UnauthorizedAccessException("Cannot write profile.json"));

        public Task BackupCorruptAsync(CancellationToken cancellationToken) =>
            Task.FromException(new UnauthorizedAccessException("Cannot write profile.json.corrupt"));
    }
}
