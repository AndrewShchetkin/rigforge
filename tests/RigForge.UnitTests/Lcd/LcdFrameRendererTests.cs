using RigForge.Application.AioDisplay;
using RigForge.Core.Models;

namespace RigForge.UnitTests.Lcd;

public sealed class LcdFrameRendererTests
{
    private static readonly TelemetrySnapshot Telemetry = new(DateTimeOffset.UtcNow,
    [
        new SensorReading("cpu", "Tctl", SensorKind.Temperature, 62.4f) { Role = SensorRole.CpuTemperature },
        new SensorReading("gpu", "GPU", SensorKind.Temperature, 45f) { Role = SensorRole.GpuTemperature },
        new SensorReading("liquid", "Liquid", SensorKind.Temperature, 31.2f) { Role = SensorRole.LiquidTemperature },
    ]);

    [Theory]
    [InlineData(LcdMode.Dashboard, 240)]
    [InlineData(LcdMode.SingleSensor, 240)]
    [InlineData(LcdMode.Graph, 240)]
    [InlineData(LcdMode.Dashboard, 640)]
    public void Renders_a_full_rgba_frame_with_content(LcdMode mode, int size)
    {
        using var renderer = new LcdFrameRenderer(size, size);
        var history = Enumerable.Range(0, 60).Select(i => 55f + 8f * MathF.Sin(i / 6f)).ToArray();

        var frame = renderer.Render(new LcdSettings { Mode = mode }, Telemetry, history);

        Assert.Equal(size * size * 4, frame.Length);
        Assert.Contains(frame.ToArray().Where((_, i) => i % 4 != 3), b => b > 200);

        // Set RIGFORGE_LCD_SNAPSHOTS to a folder to eyeball the output.
        if (Environment.GetEnvironmentVariable("RIGFORGE_LCD_SNAPSHOTS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, $"lcd-{mode}-{size}.png"), renderer.EncodePng());
        }
    }
}
