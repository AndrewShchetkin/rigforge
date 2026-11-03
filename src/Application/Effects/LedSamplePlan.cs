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

    private readonly float _gamma;
    private readonly byte _balanceR;
    private readonly byte _balanceG;
    private readonly byte _balanceB;
    private readonly byte[] _lutR = new byte[256];
    private readonly byte[] _lutG = new byte[256];
    private readonly byte[] _lutB = new byte[256];
    private int _canvasWidth;
    private int _canvasHeight;
    private float _lutBrightness = float.NaN;

    private LedSamplePlan(
        IRgbDevice device,
        IReadOnlyList<RgbZone> zones,
        int ledCount,
        int canvasWidth,
        int canvasHeight,
        DeviceColorCorrection? colorCorrection)
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
        _gamma = Math.Clamp(colorCorrection?.Gamma ?? 1f, 0.5f, 3f);
        var balance = RgbColor.TryParse(colorCorrection?.WhiteBalance, out var parsed) ? parsed : RgbColor.White;
        _balanceR = balance.R;
        _balanceG = balance.G;
        _balanceB = balance.B;
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
        int canvasHeight = 180,
        DeviceColorCorrection? colorCorrection = null) =>
        Build(device, placements.Values.ToArray(), canvasWidth, canvasHeight, colorCorrection);

    /// <summary>
    /// Builds one sample plan. Placements of the same zone are consumed in list order, which is the
    /// physical ARGB chain order. A single placement without a topology or size override keeps the
    /// hardware LED positions; segments and matrix overrides are generated and concatenated.
    /// When a segment's LED count does not fit, only the prefix of that full layout is copied so the
    /// LEDs that remain keep the positions they would have in the complete strip or matrix. LEDs past
    /// a short chain stay disabled, and the next zone is not shifted.
    /// </summary>
    public static LedSamplePlan Build(
        IRgbDevice device,
        IReadOnlyList<ZonePlacement> placements,
        int canvasWidth = 320,
        int canvasHeight = 180,
        DeviceColorCorrection? colorCorrection = null)
    {
        var zones = device.Zones;
        var plan = new LedSamplePlan(device, zones, zones.Sum(z => z.LedCount), canvasWidth, canvasHeight, colorCorrection);

        var index = 0;
        foreach (var zone in zones)
        {
            var zonePlacements = new List<ZonePlacement>();
            foreach (var placement in placements)
            {
                if (string.Equals(placement.DeviceId, device.Id, StringComparison.Ordinal)
                    && string.Equals(placement.ZoneId, zone.Id, StringComparison.Ordinal))
                {
                    zonePlacements.Add(placement);
                }
            }

            if (zonePlacements.Count == 0)
            {
                for (var i = 0; i < zone.LedCount; i++)
                {
                    plan.Points[index] = new LedPoint(0f, 0f, false);
                    index++;
                }

                continue;
            }

            if (zonePlacements.Count == 1 && UsesHardwareLayout(zone, zonePlacements[0]))
            {
                WritePlacement(plan, zone.Leds, zonePlacements[0], ref index);
                continue;
            }

            var zoneEnd = index + zone.LedCount;
            foreach (var placement in zonePlacements)
            {
                if (index >= zoneEnd)
                {
                    break;
                }

                var requested = placement.LedCount ?? (zonePlacements.Count == 1 ? zone.LedCount : 0);
                var local = LedLayouts.For(
                    placement.Topology ?? zone.Topology,
                    Math.Max(requested, 0),
                    placement.MatrixColumns ?? 8,
                    placement.Serpentine);
                var remaining = zoneEnd - index;
                if (local.Count > remaining)
                {
                    // Keep the first LEDs of the full shape. A shorter regenerated layout would
                    // stretch those LEDs across the whole placement box.
                    local = local.Take(remaining).ToArray();
                }

                WritePlacement(plan, local, placement, ref index);
            }

            while (index < zoneEnd)
            {
                plan.Points[index] = new LedPoint(0f, 0f, false);
                index++;
            }
        }

        plan.RebuildTaps(canvasWidth, canvasHeight);
        return plan;
    }

    /// <summary>True when the hardware LED positions already describe this single placement.</summary>
    private static bool UsesHardwareLayout(RgbZone zone, ZonePlacement placement) =>
        placement.Topology is null && (placement.LedCount is null || placement.LedCount == zone.LedCount);

    private static void WritePlacement(LedSamplePlan plan, IReadOnlyList<LedPosition> leds, ZonePlacement placement, ref int index)
    {
        var (sin, cos) = MathF.SinCos(placement.Rotation * MathF.PI / 180f);
        foreach (var led in leds)
        {
            // Rotate around the placement's top-left corner (Konva convention, y axis pointing down).
            var localX = led.X * placement.Width;
            var localY = led.Y * placement.Height;
            plan.X[index] = placement.X + localX * cos - localY * sin;
            plan.Y[index] = placement.Y + localX * sin + localY * cos;
            plan.Enabled[index] = placement.Enabled;
            plan.Points[index] = new LedPoint(plan.X[index], plan.Y[index], plan.Enabled[index]);
            index++;
        }
    }

    /// <summary>Samples the rendered canvas (RGBA8888, opaque) for every LED and applies the brightness.</summary>
    public void Sample(ReadOnlySpan<byte> rgba, int width, int height, float brightness)
    {
        if (width != _canvasWidth || height != _canvasHeight)
        {
            RebuildTaps(width, height);
        }

        EnsureLut(brightness);
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
            colors[i] = new RgbColor(_lutR[r], _lutG[g], _lutB[b]);
        }
    }

    /// <summary>
    /// Finishes a directly sampled frame in <see cref="Colors"/>: LEDs of disabled or unplaced zones are
    /// driven black, the rest are scaled by the global brightness. Allocation-free.
    /// </summary>
    public void ApplyDirectBrightness(float brightness)
    {
        EnsureLut(brightness);
        var points = Points;
        var colors = Colors;
        for (var i = 0; i < colors.Length; i++)
        {
            if (!points[i].Enabled)
            {
                colors[i] = RgbColor.Black;
                continue;
            }

            var color = colors[i];
            colors[i] = new RgbColor(_lutR[color.R], _lutG[color.G], _lutB[color.B]);
        }
    }

    /// <summary>
    /// Fills the three 256-entry tables for <paramref name="brightness"/>. Identity gamma and white balance
    /// match <see cref="RgbColor.Scale"/>; otherwise each channel is <c>pow(i/255*brightness, gamma) * whiteBalance</c>.
    /// Rebuilding writes into the existing arrays, so a steady brightness allocates nothing per frame.
    /// </summary>
    private void EnsureLut(float brightness)
    {
        brightness = Math.Clamp(brightness, 0f, 1f);
        if (brightness == _lutBrightness)
        {
            return;
        }

        _lutBrightness = brightness;
        if (_gamma == 1f && _balanceR == 255 && _balanceG == 255 && _balanceB == 255)
        {
            for (var i = 0; i < 256; i++)
            {
                var scaled = (byte)(i * brightness + 0.5f);
                _lutR[i] = scaled;
                _lutG[i] = scaled;
                _lutB[i] = scaled;
            }

            return;
        }

        var gainR = _balanceR / 255f;
        var gainG = _balanceG / 255f;
        var gainB = _balanceB / 255f;
        for (var i = 0; i < 256; i++)
        {
            var corrected = MathF.Pow(i / 255f * brightness, _gamma);
            _lutR[i] = ToByte(corrected * gainR);
            _lutG[i] = ToByte(corrected * gainG);
            _lutB[i] = ToByte(corrected * gainB);
        }
    }

    private static byte ToByte(float channel) => (byte)Math.Clamp(channel * 255f + 0.5f, 0f, 255f);

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
