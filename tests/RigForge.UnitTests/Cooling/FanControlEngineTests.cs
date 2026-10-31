using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RigForge.Application;
using RigForge.Application.Cooling;
using RigForge.Application.Devices;
using RigForge.Application.Effects;
using RigForge.Application.Profiles;
using RigForge.Application.Telemetry;
using RigForge.Application.Validation;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Platform;

namespace RigForge.UnitTests.Cooling;

public sealed class FanControlEngineTests
{
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeFanController _cooler = new(
        "kraken",
        new FanChannel("pump", "Pump", FanChannelKind.Pump, 20, 100),
        new FanChannel("fan", "Fans", FanChannelKind.Fan, 0, 100));

    [Fact]
    public async Task Applies_curves_and_never_drives_the_pump_below_its_minimum()
    {
        var (engine, telemetry, profiles) = await CreateAsync();
        SetLiquid(25f);
        await profiles.UpsertFanCurveAsync(Curve("pump", (20, 0), (60, 100)), TestContext.Current.CancellationToken);
        await profiles.UpsertFanCurveAsync(Curve("fan", (20, 10), (60, 50)), TestContext.Current.CancellationToken);

        await telemetry.PollOnceAsync(TestContext.Current.CancellationToken);
        await engine.TickAsync(TestContext.Current.CancellationToken);

        Assert.Equal(30, _cooler.Duties["pump"]);
        Assert.Equal(15, _cooler.Duties["fan"]);
    }

    [Fact]
    public async Task Stale_temperatures_trigger_the_failsafe()
    {
        var (engine, telemetry, profiles) = await CreateAsync();
        SetLiquid(30f);
        await profiles.UpsertFanCurveAsync(Curve("fan", (20, 10), (60, 50)), TestContext.Current.CancellationToken);
        await telemetry.PollOnceAsync(TestContext.Current.CancellationToken);

        _time.Advance(TimeSpan.FromSeconds(10));
        await engine.TickAsync(TestContext.Current.CancellationToken);

        Assert.Equal(100, _cooler.Duties["fan"]);
        Assert.True(Assert.Single(engine.Status).Failsafe);
    }

    [Fact]
    public async Task Critical_liquid_temperature_forces_every_channel_to_full_speed()
    {
        var (engine, telemetry, profiles) = await CreateAsync();
        SetLiquid(61f);
        await profiles.UpsertFanCurveAsync(Curve("fan", (20, 10), (70, 20)), TestContext.Current.CancellationToken);

        await telemetry.PollOnceAsync(TestContext.Current.CancellationToken);
        await engine.TickAsync(TestContext.Current.CancellationToken);

        Assert.Equal(100, _cooler.Duties["fan"]);
    }

    [Fact]
    public async Task Removing_a_curve_returns_the_channel_to_firmware_control()
    {
        var (engine, telemetry, profiles) = await CreateAsync();
        SetLiquid(30f);
        await profiles.UpsertFanCurveAsync(Curve("fan", (20, 10), (60, 50)), TestContext.Current.CancellationToken);
        await telemetry.PollOnceAsync(TestContext.Current.CancellationToken);
        await engine.TickAsync(TestContext.Current.CancellationToken);

        await profiles.RemoveFanCurveAsync("kraken:fan", TestContext.Current.CancellationToken);
        await engine.TickAsync(TestContext.Current.CancellationToken);

        Assert.Equal("fan", Assert.Single(_cooler.Restored));
    }

    private static FanCurve Curve(string channel, params (float Temperature, float Duty)[] points) => new()
    {
        Id = $"kraken:{channel}",
        DeviceId = "kraken",
        ChannelId = channel,
        SensorSelectors = [TelemetrySnapshot.RoleSelector(SensorRole.LiquidTemperature)],
        Points = points.Select(p => new FanCurvePoint(p.Temperature, p.Duty)).ToArray(),
        RampUpPercentPerSecond = 0,
        RampDownPercentPerSecond = 0,
    };

    private void SetLiquid(float celsius)
    {
        _cooler.Readings.Clear();
        _cooler.Readings.Add(new SensorReading("kraken/liquid", "Liquid", SensorKind.Temperature, celsius) { Role = SensorRole.LiquidTemperature });
    }

    private async Task<(FanControlEngine Engine, TelemetryService Telemetry, ProfileService Profiles)> CreateAsync()
    {
        var devices = new DeviceManager([new StaticProbe(_cooler)], NullLogger<DeviceManager>.Instance);
        await devices.ScanAsync(TestContext.Current.CancellationToken);

        var profiles = new ProfileService(
            new InMemoryProfileStore(),
            new RigProfileValidator(new EffectRegistry(new NullScreenCapturer())),
            NullLogger<ProfileService>.Instance);
        var telemetry = new TelemetryService(devices, Options.Create(new TelemetryOptions()), _time, NullLogger<TelemetryService>.Instance);
        var engine = new FanControlEngine(devices, profiles, telemetry, Options.Create(new CoolingOptions()), _time, NullLogger<FanControlEngine>.Instance);
        return (engine, telemetry, profiles);
    }
}
