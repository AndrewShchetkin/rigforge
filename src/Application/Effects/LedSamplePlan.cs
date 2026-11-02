using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Application.Effects;

/// <summary>
/// Precomputed canvas coordinates and integer bilinear taps of every LED of one device, built from the zone placements.
/// Rebuilt only when the layout, the device list or a device's zones change.
/// </summary>
internal sealed class LedSamplePlan
{
    internal readonly record struct PrecomputedTap(
        int P00,
        int P10,
        int P01,
        int P11,
        ushort W00,
        ushort W10,
        ushort W01,
        ushort W11,
        bool Enabled);

    private int _canvasWidth;
    private int _canvasHeight;

    private LedSamplePlan(IRgbDevice device, IReadOnlyList<RgbZone> zones, int ledCount, int canvasWidth, int canvasHeight)
    {
        Device = device;
        Zones = zones;
        X = new float[ledCount];
        Y = new float[ledCount];
        Enabled = new bool[ledCount];
        Points = new LedPoint[ledCount];
        Taps = new PrecomputedTap[ledCount];
        Colors = new RgbColor[ledCount];
        _canvasWidth = canvasWidth;
        _canvasHeight = canvasHeight;
    }

    public IRgbDevice Device { get; }

    /// <summary>The zone list the plan was built from (reference compared to detect resizes).</summary>
    public IReadOnlyList<RgbZone> Zones { get; }

    public float[] X { get; }

    public float[] Y { get; }

    /// <summary>False for LEDs of disabled or unplaced zones; they are driven black.</summary>
    public bool[] Enabled { get; }

    /// <summary>Canvas position and enabled flag of every LED, for Direct LED Sampling without rasterization.</summary>
    public LedPoint[] Points { get; }

    public PrecomputedTap[] Taps { get; }

    /// <summary>Reusable output buffer for the sampled frame.</summary>
    public RgbColor[] Colors { get; }

    public bool IsStale => !ReferenceEquals(Zones, Device.Zones);

    public static LedSamplePlan Build(
        IRgbDevice device,
        IReadOnlyDictionary<(string DeviceId, string ZoneId), ZonePlacement> placements,
        int canvasWidth = 320,
        int canvasHeight = 180)
    {
        var zones = device.Zones;
        var plan = new LedSamplePlan(device, zones, zones.Sum(z => z.LedCount), canvasWidth, canvasHeight);

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
                plan.Points[index] = new LedPoint(plan.X[index], plan.Y[index], plan.Enabled[index]);
                index++;
            }
        }

        plan.RebuildTaps(canvasWidth, canvasHeight);
        return plan;
    }

    /// <summary>Samples the rendered canvas (RGBA8888, opaque) for every LED and applies the brightness.</summary>
    public void Sample(ReadOnlySpan<byte> rgba, int width, int height, float brightness)
    {
        if (width != _canvasWidth || height != _canvasHeight)
        {
            RebuildTaps(width, height);
        }

        var taps = Taps;
        var colors = Colors;
        for (var i = 0; i < colors.Length; i++)
        {
            ref readonly var tap = ref taps[i];
            if (!tap.Enabled)
            {
                colors[i] = RgbColor.Black;
                continue;
            }

            var r = (byte)((rgba[tap.P00] * tap.W00 + rgba[tap.P10] * tap.W10 + rgba[tap.P01] * tap.W01 + rgba[tap.P11] * tap.W11 + 128) >> 8);
            var g = (byte)((rgba[tap.P00 + 1] * tap.W00 + rgba[tap.P10 + 1] * tap.W10 + rgba[tap.P01 + 1] * tap.W01 + rgba[tap.P11 + 1] * tap.W11 + 128) >> 8);
            var b = (byte)((rgba[tap.P00 + 2] * tap.W00 + rgba[tap.P10 + 2] * tap.W10 + rgba[tap.P01 + 2] * tap.W01 + rgba[tap.P11 + 2] * tap.W11 + 128) >> 8);
            colors[i] = new RgbColor(r, g, b).Scale(brightness);
        }
    }

    /// <summary>
    /// Finishes a directly sampled frame in <see cref="Colors"/>: LEDs of disabled or unplaced zones are
    /// driven black, the rest are scaled by the global brightness. Allocation-free.
    /// </summary>
    public void ApplyDirectBrightness(float brightness)
    {
        var points = Points;
        var colors = Colors;
        for (var i = 0; i < colors.Length; i++)
        {
            colors[i] = points[i].Enabled ? colors[i].Scale(brightness) : RgbColor.Black;
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

    private void RebuildTaps(int width, int height)
    {
        _canvasWidth = width;
        _canvasHeight = height;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        for (var i = 0; i < Taps.Length; i++)
        {
            if (!Enabled[i])
            {
                Taps[i] = default;
                continue;
            }

            var x = Math.Clamp(X[i] - 0.5f, 0f, width - 1);
            var y = Math.Clamp(Y[i] - 0.5f, 0f, height - 1);

            var x0 = (int)x;
            var y0 = (int)y;
            var x1 = Math.Min(x0 + 1, width - 1);
            var y1 = Math.Min(y0 + 1, height - 1);

            var fx256 = (int)((x - x0) * 256f + 0.5f);
            var fy256 = (int)((y - y0) * 256f + 0.5f);
            var invFx = 256 - fx256;
            var invFy = 256 - fy256;

            var w00 = (ushort)((invFx * invFy + 128) >> 8);
            var w10 = (ushort)Math.Min((fx256 * invFy + 128) >> 8, 256 - w00);
            var w01 = (ushort)Math.Min((invFx * fy256 + 128) >> 8, 256 - w00 - w10);
            var w11 = (ushort)(256 - w00 - w10 - w01);

            var p00 = (y0 * width + x0) * 4;
            var p10 = (y0 * width + x1) * 4;
            var p01 = (y1 * width + x0) * 4;
            var p11 = (y1 * width + x1) * 4;

            Taps[i] = new PrecomputedTap(p00, p10, p01, p11, w00, w10, w01, w11, Enabled: true);
        }
    }

    private static byte Blend(ReadOnlySpan<byte> rgba, int p00, int p10, int p01, int p11, int channel, float fx, float fy)
    {
        var top = rgba[p00 + channel] + (rgba[p10 + channel] - rgba[p00 + channel]) * fx;
        var bottom = rgba[p01 + channel] + (rgba[p11 + channel] - rgba[p01 + channel]) * fx;
        return (byte)(top + (bottom - top) * fy + 0.5f);
    }
}
