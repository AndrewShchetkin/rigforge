using System.Globalization;
using RigForge.Core.Models;
using SkiaSharp;

namespace RigForge.Application.AioDisplay;

/// <summary>
/// Draws the telemetry widgets for a square/round AIO screen. All drawing happens in a 240 x 240 design space
/// that is scaled to the device resolution (240, 320 or 640 px).
/// </summary>
internal sealed class LcdFrameRenderer : IDisposable
{
    private const float DesignSize = 240f;
    private const float Center = DesignSize / 2f;

    private static readonly SKColor Track = new(0x27, 0x27, 0x2A);
    private static readonly SKColor Muted = new(0xA1, 0xA1, 0xAA);

    private readonly SKBitmap _bitmap;
    private readonly SKCanvas _canvas;
    private readonly byte[] _rgba;
    private readonly SKTypeface _typeface;
    private readonly SKFont _hugeFont;
    private readonly SKFont _largeFont;
    private readonly SKFont _mediumFont;
    private readonly SKFont _smallFont;
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };

    public LcdFrameRenderer(int width, int height)
    {
        Width = width;
        Height = height;
        _bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        _canvas = new SKCanvas(_bitmap);
        _rgba = new byte[width * height * 4];
        _typeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold) ?? SKTypeface.Default;
        _hugeFont = new SKFont(_typeface, 64f);
        _largeFont = new SKFont(_typeface, 44f);
        _mediumFont = new SKFont(_typeface, 20f);
        _smallFont = new SKFont(_typeface, 13f);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Renders a frame and returns it as RGBA8888 (buffer is reused by the next call).</summary>
    public ReadOnlyMemory<byte> Render(LcdSettings settings, TelemetrySnapshot telemetry, float[] history)
    {
        var accent = RgbColor.TryParse(settings.AccentColor, out var color) ? new SKColor(color.R, color.G, color.B) : SKColors.MediumPurple;

        _canvas.Save();
        _canvas.Clear(SKColors.Black);
        _canvas.RotateDegrees(settings.Rotation, Width / 2f, Height / 2f);
        _canvas.Scale(Width / DesignSize, Height / DesignSize);

        var sensors = settings.SensorSelectors.Select(s => telemetry.TryResolve(s, out var r) ? r : null).ToList();
        var primary = sensors.FirstOrDefault();

        switch (settings.Mode)
        {
            case LcdMode.Dashboard:
                DrawGauge(primary, accent, thickness: 14f);
                DrawPrimaryValue(primary, _largeFont, 128f);
                DrawSecondary(sensors.Skip(1).Take(2).ToList());
                break;
            case LcdMode.SingleSensor:
                DrawGauge(primary, accent, thickness: 18f);
                DrawPrimaryValue(primary, _hugeFont, 140f);
                break;
            case LcdMode.Graph:
                DrawGraph(primary, history, accent);
                break;
            default:
                DrawCenteredText("Firmware screen", Center, Center + 6f, _mediumFont, Muted);
                break;
        }

        _canvas.Restore();
        _canvas.Flush();

        _bitmap.GetPixelSpan().CopyTo(_rgba);
        return _rgba;
    }

    public byte[] EncodePng()
    {
        using var image = SKImage.FromBitmap(_bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public void Dispose()
    {
        _fill.Dispose();
        _stroke.Dispose();
        _hugeFont.Dispose();
        _largeFont.Dispose();
        _mediumFont.Dispose();
        _smallFont.Dispose();
        _typeface.Dispose();
        _canvas.Dispose();
        _bitmap.Dispose();
    }

    private void DrawGauge(SensorReading? reading, SKColor accent, float thickness)
    {
        const float startAngle = 135f;
        const float sweep = 270f;
        var radius = 104f - thickness / 2f;
        var oval = new SKRect(Center - radius, Center - radius, Center + radius, Center + radius);

        _stroke.StrokeWidth = thickness;
        _stroke.Color = Track;
        _canvas.DrawArc(oval, startAngle, sweep, false, _stroke);

        if (reading is null)
        {
            return;
        }

        var fraction = GaugeFraction(reading);
        if (fraction <= 0f)
        {
            return;
        }

        _stroke.Color = reading.Kind == SensorKind.Temperature ? TemperatureColor(reading.Value, accent) : accent;
        _canvas.DrawArc(oval, startAngle, sweep * fraction, false, _stroke);
    }

    private void DrawPrimaryValue(SensorReading? reading, SKFont valueFont, float baseline)
    {
        DrawCenteredText(Label(reading), Center, 74f, _smallFont, Muted);
        if (reading is null)
        {
            DrawCenteredText("--", Center, baseline, valueFont, SKColors.White);
            return;
        }

        var value = FormatValue(reading);
        var unit = reading.Kind == SensorKind.Temperature ? "°" : reading.Unit;
        var valueWidth = valueFont.MeasureText(value);
        var unitWidth = _mediumFont.MeasureText(unit);
        var left = Center - (valueWidth + unitWidth) / 2f;

        _fill.Color = SKColors.White;
        _canvas.DrawText(value, left, baseline, SKTextAlign.Left, valueFont, _fill);
        _fill.Color = Muted;
        _canvas.DrawText(unit, left + valueWidth + 2f, baseline - valueFont.Size * 0.45f, SKTextAlign.Left, _mediumFont, _fill);
    }

    private void DrawSecondary(IReadOnlyList<SensorReading?> readings)
    {
        if (readings.Count == 0)
        {
            return;
        }

        var spacing = readings.Count == 1 ? 0f : 64f;
        for (var i = 0; i < readings.Count; i++)
        {
            var x = Center + (i - (readings.Count - 1) / 2f) * spacing;
            var reading = readings[i];
            DrawCenteredText(Label(reading), x, 168f, _smallFont, Muted);
            DrawCenteredText(reading is null ? "--" : FormatValue(reading) + (reading.Kind == SensorKind.Temperature ? "°" : reading.Unit), x, 190f, _mediumFont, SKColors.White);
        }
    }

    private void DrawGraph(SensorReading? reading, float[] history, SKColor accent)
    {
        DrawCenteredText(Label(reading), Center, 52f, _smallFont, Muted);
        DrawCenteredText(reading is null ? "--" : FormatValue(reading) + (reading.Kind == SensorKind.Temperature ? "°C" : reading.Unit), Center, 88f, _largeFont, SKColors.White);

        var area = new SKRect(36f, 108f, 204f, 188f);
        _stroke.StrokeWidth = 1f;
        _stroke.Color = Track;
        _canvas.DrawRect(area, _stroke);

        if (history.Length < 2)
        {
            return;
        }

        var min = history.Min();
        var max = history.Max();
        var padding = Math.Max((max - min) * 0.15f, 1f);
        min -= padding;
        max += padding;

        using var builder = new SKPathBuilder();
        for (var i = 0; i < history.Length; i++)
        {
            var x = area.Left + area.Width * i / (history.Length - 1);
            var y = area.Bottom - area.Height * (history[i] - min) / (max - min);
            if (i == 0)
            {
                builder.MoveTo(x, y);
            }
            else
            {
                builder.LineTo(x, y);
            }
        }

        using var line = builder.Snapshot();
        builder.LineTo(area.Right, area.Bottom);
        builder.LineTo(area.Left, area.Bottom);
        builder.Close();
        using var fillPath = builder.Detach();

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, area.Top), new SKPoint(0, area.Bottom),
            [accent.WithAlpha(0x90), accent.WithAlpha(0x00)], null, SKShaderTileMode.Clamp);
        _fill.Shader = shader;
        _canvas.DrawPath(fillPath, _fill);
        _fill.Shader = null;

        _stroke.StrokeWidth = 2.5f;
        _stroke.Color = accent;
        _canvas.DrawPath(line, _stroke);

        DrawCenteredText(max.ToString("0", CultureInfo.InvariantCulture), 22f, area.Top + 10f, _smallFont, Muted);
        DrawCenteredText(min.ToString("0", CultureInfo.InvariantCulture), 22f, area.Bottom, _smallFont, Muted);
    }

    private void DrawCenteredText(string text, float x, float baseline, SKFont font, SKColor color)
    {
        _fill.Color = color;
        _canvas.DrawText(text, x, baseline, SKTextAlign.Center, font, _fill);
    }

    private static float GaugeFraction(SensorReading reading) => reading.Kind switch
    {
        SensorKind.Temperature => Math.Clamp((reading.Value - 20f) / 80f, 0f, 1f),
        SensorKind.Load or SensorKind.Duty => Math.Clamp(reading.Value / 100f, 0f, 1f),
        SensorKind.FanSpeed => Math.Clamp(reading.Value / 3000f, 0f, 1f),
        _ => 0f,
    };

    private static SKColor TemperatureColor(float celsius, SKColor accent) => celsius switch
    {
        >= 85f => new SKColor(0xEF, 0x44, 0x44),
        >= 70f => new SKColor(0xF5, 0x9E, 0x0B),
        _ => accent,
    };

    private static string FormatValue(SensorReading reading) => reading.Kind switch
    {
        SensorKind.Temperature when reading.Role == SensorRole.LiquidTemperature => reading.Value.ToString("0.0", CultureInfo.InvariantCulture),
        SensorKind.Temperature or SensorKind.Load or SensorKind.Duty => reading.Value.ToString("0", CultureInfo.InvariantCulture),
        SensorKind.Power => reading.Value.ToString("0", CultureInfo.InvariantCulture),
        _ => reading.Value.ToString("0", CultureInfo.InvariantCulture),
    };

    private static string Label(SensorReading? reading) => reading?.Role switch
    {
        null => "NO DATA",
        SensorRole.CpuTemperature => "CPU",
        SensorRole.GpuTemperature => "GPU",
        SensorRole.LiquidTemperature => "LIQUID",
        SensorRole.CpuLoad => "CPU LOAD",
        SensorRole.GpuLoad => "GPU LOAD",
        SensorRole.GpuPower => "GPU POWER",
        SensorRole.PumpSpeed => "PUMP",
        _ => reading.Name.Length > 12 ? reading.Name[..12].ToUpperInvariant() : reading.Name.ToUpperInvariant(),
    };
}
