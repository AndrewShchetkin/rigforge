using RigForge.Application.Effects;
using RigForge.Application.Validation;
using RigForge.Core.Models;
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
        ]);

        Assert.True(snapshot.TryResolve("role:cputemperature", out var reading));
        Assert.Equal("a/cpu", reading.Id);
        Assert.True(snapshot.TryResolve("z/cpu", out _));
        Assert.False(snapshot.TryResolve("role:GpuTemperature", out _));
    }

    [Fact]
    public void Proc_stat_cpu_line_is_parsed()
    {
        var parsed = LinuxSystemDevice.ParseCpuLine("cpu  300 0 200 400 50 30 20 0 0 0");

        Assert.NotNull(parsed);
        Assert.Equal(450UL, parsed.Value.Idle);
        Assert.Equal(1000UL, parsed.Value.Total);
    }
}
