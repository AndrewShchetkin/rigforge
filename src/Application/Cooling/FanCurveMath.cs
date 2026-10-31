using RigForge.Core.Models;

namespace RigForge.Application.Cooling;

public static class FanCurveMath
{
    /// <summary>Piecewise-linear interpolation of the curve; flat before the first and after the last point.</summary>
    public static float Evaluate(IReadOnlyList<FanCurvePoint> points, float temperature)
    {
        if (points.Count == 0)
        {
            return 100f;
        }

        var sorted = IsSorted(points) ? points : points.OrderBy(p => p.Temperature).ToArray();

        if (temperature <= sorted[0].Temperature)
        {
            return sorted[0].Duty;
        }

        for (var i = 1; i < sorted.Count; i++)
        {
            var upper = sorted[i];
            if (temperature > upper.Temperature)
            {
                continue;
            }

            var lower = sorted[i - 1];
            var span = upper.Temperature - lower.Temperature;
            if (span <= 0f)
            {
                return upper.Duty;
            }

            var t = (temperature - lower.Temperature) / span;
            return lower.Duty + (upper.Duty - lower.Duty) * t;
        }

        return sorted[^1].Duty;
    }

    private static bool IsSorted(IReadOnlyList<FanCurvePoint> points)
    {
        for (var i = 1; i < points.Count; i++)
        {
            if (points[i].Temperature < points[i - 1].Temperature)
            {
                return false;
            }
        }

        return true;
    }
}
