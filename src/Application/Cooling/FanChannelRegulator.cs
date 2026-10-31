using RigForge.Core.Models;

namespace RigForge.Application.Cooling;

public readonly record struct RegulatorOutput(float TargetDuty, float Duty, float? Temperature, bool Failsafe);

/// <summary>
/// Turns temperature samples into a smooth duty for one channel:
/// exponential smoothing → curve → hysteresis → ramp limiting. Pure logic; time is passed in.
/// </summary>
/// <remarks>
/// Hysteresis is asymmetric: a rising temperature re-evaluates the curve immediately (heat must be handled
/// at once), a falling temperature only lowers the duty after dropping <see cref="FanCurve.HysteresisCelsius"/>
/// below the temperature that produced the current target. This removes audible speed hunting around a set point.
/// </remarks>
public sealed class FanChannelRegulator
{
    private const float MaxStepSeconds = 10f;

    private FanCurve? _curve;
    private float? _smoothedTemperature;
    private float? _anchorTemperature;
    private float _curveTarget;
    private float? _output;
    private DateTimeOffset? _lastUpdate;

    public RegulatorOutput Update(FanCurve curve, float? temperature, int failsafeDuty, bool forceFailsafe, DateTimeOffset now)
    {
        var elapsedSeconds = _lastUpdate is { } last ? Math.Clamp((float)(now - last).TotalSeconds, 0f, MaxStepSeconds) : 0f;
        _lastUpdate = now;

        if (!ReferenceEquals(curve, _curve))
        {
            // Edited curve: forget the hysteresis anchor so the new points apply right away.
            _curve = curve;
            _anchorTemperature = null;
        }

        var failsafe = forceFailsafe || (curve.Mode == FanControlMode.Curve && temperature is null);
        if (failsafe)
        {
            // Safety beats smoothness: jump straight to the failsafe duty and re-evaluate from scratch afterwards.
            _anchorTemperature = null;
            _output = failsafeDuty;
            return new RegulatorOutput(failsafeDuty, failsafeDuty, temperature, Failsafe: true);
        }

        float target;
        if (curve.Mode == FanControlMode.Fixed)
        {
            target = curve.FixedDuty;
        }
        else
        {
            var smoothed = Smooth(temperature!.Value, curve.TemperatureSmoothingSeconds, elapsedSeconds);
            if (_anchorTemperature is not { } anchor || smoothed > anchor || smoothed <= anchor - curve.HysteresisCelsius)
            {
                _curveTarget = FanCurveMath.Evaluate(curve.Points, smoothed);
                _anchorTemperature = smoothed;
            }

            target = _curveTarget;
        }

        target = Math.Clamp(target, curve.MinDuty, Math.Max(curve.MinDuty, curve.MaxDuty));
        _output = Ramp(_output, target, curve, elapsedSeconds);

        return new RegulatorOutput(target, _output.Value, _smoothedTemperature ?? temperature, Failsafe: false);
    }

    private float Smooth(float temperature, float timeConstantSeconds, float elapsedSeconds)
    {
        if (_smoothedTemperature is not { } previous || timeConstantSeconds <= 0f)
        {
            _smoothedTemperature = temperature;
            return temperature;
        }

        var alpha = 1f - MathF.Exp(-elapsedSeconds / timeConstantSeconds);
        _smoothedTemperature = previous + (temperature - previous) * alpha;
        return _smoothedTemperature.Value;
    }

    private static float Ramp(float? current, float target, FanCurve curve, float elapsedSeconds)
    {
        if (current is not { } value)
        {
            return target;
        }

        var delta = target - value;
        if (delta > 0f && curve.RampUpPercentPerSecond > 0f)
        {
            delta = Math.Min(delta, curve.RampUpPercentPerSecond * elapsedSeconds);
        }
        else if (delta < 0f && curve.RampDownPercentPerSecond > 0f)
        {
            delta = Math.Max(delta, -curve.RampDownPercentPerSecond * elapsedSeconds);
        }

        return value + delta;
    }
}
