using RigForge.Application.Cooling;
using RigForge.Core.Models;

namespace RigForge.UnitTests.Cooling;

public sealed class FanCurveMathTests
{
    private static readonly FanCurvePoint[] Points = [new(30, 20), new(50, 40), new(70, 100)];

    [Theory]
    [InlineData(10f, 20f)]
    [InlineData(30f, 20f)]
    [InlineData(40f, 30f)]
    [InlineData(60f, 70f)]
    [InlineData(90f, 100f)]
    public void Evaluate_interpolates_linearly_and_clamps_outside_the_curve(float temperature, float expected) =>
        Approx.Equal(expected, FanCurveMath.Evaluate(Points, temperature));

    [Fact]
    public void Evaluate_accepts_unsorted_points() =>
        Approx.Equal(30f, FanCurveMath.Evaluate([new(50, 40), new(30, 20)], 40f));
}

public sealed class FanChannelRegulatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static FanCurve Curve(float hysteresis = 2f, float rampUp = 0f, float rampDown = 0f) => new()
    {
        Id = "c",
        DeviceId = "d",
        ChannelId = "fan",
        Points = [new(30, 20), new(70, 100)],
        HysteresisCelsius = hysteresis,
        RampUpPercentPerSecond = rampUp,
        RampDownPercentPerSecond = rampDown,
    };

    [Fact]
    public void Rising_temperature_raises_the_duty_immediately()
    {
        var regulator = new FanChannelRegulator();
        var curve = Curve();

        regulator.Update(curve, 40f, 100, false, Start);
        var output = regulator.Update(curve, 50f, 100, false, Start.AddSeconds(1));

        Approx.Equal(60f, output.Duty);
    }

    [Fact]
    public void Small_drops_inside_the_hysteresis_band_keep_the_duty()
    {
        var regulator = new FanChannelRegulator();
        var curve = Curve(hysteresis: 3f);

        regulator.Update(curve, 50f, 100, false, Start);
        var output = regulator.Update(curve, 48f, 100, false, Start.AddSeconds(1));

        Approx.Equal(60f, output.Duty);
    }

    [Fact]
    public void Drops_beyond_the_hysteresis_band_lower_the_duty()
    {
        var regulator = new FanChannelRegulator();
        var curve = Curve(hysteresis: 3f);

        regulator.Update(curve, 50f, 100, false, Start);
        var output = regulator.Update(curve, 46f, 100, false, Start.AddSeconds(1));

        Approx.Equal(52f, output.Duty);
    }

    [Fact]
    public void Ramp_rates_limit_the_change_per_second()
    {
        var regulator = new FanChannelRegulator();
        var curve = Curve(rampUp: 10f, rampDown: 5f);

        regulator.Update(curve, 30f, 100, false, Start);
        var up = regulator.Update(curve, 70f, 100, false, Start.AddSeconds(2));
        Approx.Equal(40f, up.Duty);
        Approx.Equal(100f, up.TargetDuty);

        var down = regulator.Update(curve, 30f, 100, false, Start.AddSeconds(3));
        Approx.Equal(35f, down.Duty);
    }

    [Fact]
    public void Missing_temperature_jumps_to_the_failsafe_duty()
    {
        var regulator = new FanChannelRegulator();
        var curve = Curve(rampUp: 1f);

        regulator.Update(curve, 30f, 100, false, Start);
        var output = regulator.Update(curve, null, 100, false, Start.AddSeconds(1));

        Assert.True(output.Failsafe);
        Approx.Equal(100f, output.Duty);
    }

    [Fact]
    public void Forced_failsafe_overrides_the_curve()
    {
        var output = new FanChannelRegulator().Update(Curve(), 30f, 100, forceFailsafe: true, Start);

        Assert.True(output.Failsafe);
        Approx.Equal(100f, output.Duty);
    }

    [Fact]
    public void Fixed_mode_does_not_need_a_temperature()
    {
        var curve = Curve() with { Mode = FanControlMode.Fixed, FixedDuty = 45 };

        var output = new FanChannelRegulator().Update(curve, null, 100, false, Start);

        Assert.False(output.Failsafe);
        Approx.Equal(45f, output.Duty);
    }

    [Fact]
    public void An_edited_curve_applies_without_waiting_for_the_hysteresis()
    {
        var regulator = new FanChannelRegulator();
        regulator.Update(Curve(hysteresis: 5f), 50f, 100, false, Start);

        var quieter = Curve(hysteresis: 5f) with { Points = [new(30, 10), new(70, 50)] };
        var output = regulator.Update(quieter, 50f, 100, false, Start.AddSeconds(1));

        Approx.Equal(30f, output.Duty);
    }

    [Fact]
    public void Duty_is_clamped_to_the_curve_limits()
    {
        var curve = Curve() with { MinDuty = 35, MaxDuty = 80 };
        var regulator = new FanChannelRegulator();

        Approx.Equal(35f, regulator.Update(curve, 20f, 100, false, Start).Duty);
        Approx.Equal(80f, regulator.Update(curve, 90f, 100, false, Start.AddSeconds(1)).Duty);
    }
}
