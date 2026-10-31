using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Application.Effects;

/// <summary>
/// Precomputed canvas coordinates of every LED of one device, built from the zone placements.
/// Rebuilt only when the layout, the device list or a device's zones change.
/// </summary>
internal sealed class LedSamplePlan
{
    private LedSamplePlan(IRgbDevice device, IReadOnlyList<RgbZone> zones, int ledCount)
    {
        Device = device;
        Zones = zones;
        X = new float[ledCount];
        Y = new float[ledCount];
        Enabled = new bool[ledCount];
        Colors = new RgbColor[ledCount];
    }

    public IRgbDevice Device { get; }

    /// <summary>The zone list the plan was built from (reference compared to detect resizes).</summary>
    public IReadOnlyList<RgbZone> Zones { get; }

    public float[] X { get; }

    public float[] Y { get; }

    /// <summary>False for LEDs of disabled or unplaced zones; they are driven black.</summary>
    public bool[] Enabled { get; }

    /// <summary>Reusable output buffer for the sampled frame.</summary>
    public RgbColor[] Colors { get; }

    public bool IsStale => !ReferenceEquals(Zones, Device.Zones);

    public static LedSamplePlan Build(IRgbDevice device, IReadOnlyDictionary<(string DeviceId, string ZoneId), ZonePlacement> placements)
    {
        var zones = device.Zones;
        var plan = new LedSamplePlan(device, zones, zones.Sum(z => z.LedCount));

        var index = 0;
        foreach (var zone in zones)
        {
            placements.TryGetValue((device.Id, zone.Id), out var placement);
            var (sin, cos) = placement is null ? (0f, 1f) : MathF.SinCos(placement.Rotation * MathF.PI / 180f);

            foreach (var led in zone.Leds)
            {
                if (placement is not null)
                {
                    // Rotate around the placement's top-left corner (Konva convention, y axis pointing down).
                    var localX = led.X * placement.Width;
                    var localY = led.Y * placement.Height;
                    plan.X[index] = placement.X + localX * cos - localY * sin;
                    plan.Y[index] = placement.Y + localX * sin + localY * cos;
                }

                plan.Enabled[index] = placement is { Enabled: true };
                index++;
            }
        }

        return plan;
    }

    /// <summary>Samples the rendered canvas (RGBA8888, opaque) for every LED and applies the brightness.</summary>
    public void Sample(ReadOnlySpan<byte> rgba, int width, int height, float brightness)
    {
        for (var i = 0; i < Colors.Length; i++)
        {
            Colors[i] = Enabled[i] ? SampleBilinear(rgba, width, height, X[i], Y[i]).Scale(brightness) : RgbColor.Black;
        }
    }

    internal static RgbColor SampleBilinear(ReadOnlySpan<byte> rgba, int width, int height, float x, float y)
    {
        // Pixel centers sit at +0.5.
        x = Math.Clamp(x - 0.5f, 0f, width - 1);
        y = Math.Clamp(y - 0.5f, 0f, height - 1);

        var x0 = (int)x;
        var y0 = (int)y;
        var x1 = Math.Min(x0 + 1, width - 1);
        var y1 = Math.Min(y0 + 1, height - 1);
        var fx = x - x0;
        var fy = y - y0;

        var p00 = (y0 * width + x0) * 4;
        var p10 = (y0 * width + x1) * 4;
        var p01 = (y1 * width + x0) * 4;
        var p11 = (y1 * width + x1) * 4;

        return new RgbColor(
            Blend(rgba, p00, p10, p01, p11, 0, fx, fy),
            Blend(rgba, p00, p10, p01, p11, 1, fx, fy),
            Blend(rgba, p00, p10, p01, p11, 2, fx, fy));
    }

    private static byte Blend(ReadOnlySpan<byte> rgba, int p00, int p10, int p01, int p11, int channel, float fx, float fy)
    {
        var top = rgba[p00 + channel] + (rgba[p10 + channel] - rgba[p00 + channel]) * fx;
        var bottom = rgba[p01 + channel] + (rgba[p11 + channel] - rgba[p01 + channel]) * fx;
        return (byte)(top + (bottom - top) * fy + 0.5f);
    }
}
