namespace RigForge.Core.Models;

/// <summary>Factory for normalized LED layouts of common accessory shapes.</summary>
public static class LedLayouts
{
    private const float RingRadius = 0.45f;

    public static IReadOnlyList<LedPosition> Point() => [new LedPosition(0.5f, 0.5f)];

    public static IReadOnlyList<LedPosition> Linear(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        if (count == 1)
        {
            return Point();
        }

        var leds = new LedPosition[count];
        for (var i = 0; i < count; i++)
        {
            leds[i] = new LedPosition(i / (float)(count - 1), 0.5f);
        }

        return leds;
    }

    /// <summary>LEDs on a circle, clockwise in screen space, starting at <paramref name="startAngleDegrees"/> (-90 = top).</summary>
    public static IReadOnlyList<LedPosition> Ring(int count, float startAngleDegrees = -90f)
    {
        if (count <= 0)
        {
            return [];
        }

        var leds = new LedPosition[count];
        for (var i = 0; i < count; i++)
        {
            var radians = (startAngleDegrees + 360f * i / count) * MathF.PI / 180f;
            leds[i] = new LedPosition(0.5f + RingRadius * MathF.Cos(radians), 0.5f + RingRadius * MathF.Sin(radians));
        }

        return leds;
    }

    /// <summary>
    /// Row-major matrix. <paramref name="columns"/> is clamped to 1..count. Cell centers are
    /// <c>((col + 0.5) / columns, (row + 0.5) / rows)</c>. When <paramref name="serpentine"/> is set,
    /// odd rows are reversed so a zigzag LED strip maps onto the grid.
    /// </summary>
    public static IReadOnlyList<LedPosition> Matrix2D(int count, int columns, bool serpentine = false)
    {
        if (count <= 0)
        {
            return [];
        }

        columns = Math.Clamp(columns, 1, Math.Max(1, count));
        var rows = (count + columns - 1) / columns;
        var leds = new LedPosition[count];
        for (var i = 0; i < count; i++)
        {
            var row = i / columns;
            var col = i % columns;
            if (serpentine && (row & 1) == 1)
            {
                col = columns - 1 - col;
            }

            leds[i] = new LedPosition((col + 0.5f) / columns, (row + 0.5f) / rows);
        }

        return leds;
    }

    /// <summary>Lays out daisy-chained segments (e.g. three fans on one channel) side by side, left to right.</summary>
    public static IReadOnlyList<LedPosition> Row(IReadOnlyList<(LedTopology Topology, int LedCount)> segments)
    {
        if (segments.Count == 0)
        {
            return [];
        }

        var result = new List<LedPosition>(segments.Sum(s => s.LedCount));
        for (var index = 0; index < segments.Count; index++)
        {
            var (topology, count) = segments[index];
            var local = topology switch
            {
                LedTopology.Ring => Ring(count),
                LedTopology.Point => count == 1 ? Point() : Linear(count),
                _ => Linear(count),
            };

            foreach (var led in local)
            {
                result.Add(new LedPosition((index + led.X) / segments.Count, led.Y));
            }
        }

        return result;
    }

    public static IReadOnlyList<LedPosition> For(LedTopology topology, int count, int matrixColumns = 8, bool serpentine = false) => topology switch
    {
        LedTopology.Ring => Ring(count),
        LedTopology.Point when count == 1 => Point(),
        LedTopology.Matrix2D => Matrix2D(count, matrixColumns, serpentine),
        _ => Linear(count),
    };
}
