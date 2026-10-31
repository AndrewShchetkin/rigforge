using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Application.Devices;
using RigForge.Application.Profiles;
using RigForge.Application.Telemetry;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Application.AioDisplay;

/// <summary>
/// Renders telemetry widgets for every AIO screen and streams them at <see cref="LcdSettings.FramesPerSecond"/>.
/// Unchanged frames are not re-uploaded (except for a keep-alive), and on shutdown the screens are handed
/// back to the firmware so they do not freeze on a stale frame.
/// </summary>
public sealed partial class LcdRenderEngine(
    DeviceManager devices,
    ProfileService profiles,
    TelemetryService telemetry,
    IOptions<LcdOptions> options,
    TimeProvider timeProvider,
    ILogger<LcdRenderEngine> logger)
{
    private const int PreviewSize = 240;
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);

    private readonly SensorHistory _history = new(options.Value.HistorySamples);
    private readonly HashSet<string> _failingDevices = new(StringComparer.Ordinal);
    private volatile bool _previewRequested;

    /// <summary>Raised with a PNG of the latest frame while <see cref="PreviewRequested"/> is set.</summary>
    public event Action<byte[]>? PreviewRendered;

    public bool PreviewRequested
    {
        get => _previewRequested;
        set => _previewRequested = value;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        telemetry.Updated += RecordHistory;
        var renderers = new Dictionary<(int Width, int Height), LcdFrameRenderer>();
        var state = new LoopState();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var settings = profiles.Current.Lcd;
                var lcds = devices.LcdDevices.ToList();

                if (!ReferenceEquals(state.Devices, devices.Devices))
                {
                    state = new LoopState { Devices = devices.Devices };
                }

                if (state.Brightness != settings.Brightness)
                {
                    foreach (var lcd in lcds)
                    {
                        await RunSafeAsync(lcd, (d, ct) => d.SetBrightnessAsync(settings.Brightness, ct), cancellationToken);
                    }

                    state.Brightness = settings.Brightness;
                }

                await RenderAndPushAsync(settings, lcds, renderers, state, cancellationToken);

                var deviceCap = lcds.Count > 0 ? lcds.Min(l => l.Lcd.MaxFramesPerSecond) : options.Value.MaxFramesPerSecond;
                var fps = Math.Clamp(settings.FramesPerSecond, 0.1f, Math.Min(options.Value.MaxFramesPerSecond, deviceCap));
                await Task.Delay(TimeSpan.FromSeconds(1 / fps), timeProvider, cancellationToken);
            }
        }
        finally
        {
            telemetry.Updated -= RecordHistory;
            foreach (var renderer in renderers.Values)
            {
                renderer.Dispose();
            }

            using var timeout = new CancellationTokenSource(ShutdownTimeout);
            foreach (var lcd in devices.LcdDevices)
            {
                await RunSafeAsync(lcd, (d, ct) => d.ShowBuiltInScreenAsync(ct), timeout.Token);
            }
        }
    }

    private async Task RenderAndPushAsync(
        LcdSettings settings,
        List<IAioLcdDevice> lcds,
        Dictionary<(int Width, int Height), LcdFrameRenderer> renderers,
        LoopState state,
        CancellationToken cancellationToken)
    {
        var snapshot = telemetry.Latest;
        var now = timeProvider.GetUtcNow();

        if (settings.Mode == LcdMode.BuiltInLiquid)
        {
            if (!state.BuiltInActive)
            {
                foreach (var lcd in lcds)
                {
                    await RunSafeAsync(lcd, (d, ct) => d.ShowBuiltInScreenAsync(ct), cancellationToken);
                }

                state.BuiltInActive = true;
            }

            if (_previewRequested)
            {
                var preview = GetRenderer(renderers, PreviewSize, PreviewSize);
                preview.Render(settings, snapshot, []);
                PreviewRendered?.Invoke(preview.EncodePng());
            }

            return;
        }

        state.BuiltInActive = false;
        var contentChanged = !ReferenceEquals(settings, state.Settings) || snapshot.Timestamp != state.TelemetryTimestamp;
        var keepAliveDue = now - state.LastPushAt >= options.Value.KeepAliveInterval;
        var history = _history.Snapshot();
        byte[]? png = null;

        foreach (var group in lcds.GroupBy(l => (l.Lcd.Width, l.Lcd.Height)))
        {
            var renderer = GetRenderer(renderers, group.Key.Width, group.Key.Height);
            var frame = renderer.Render(settings, snapshot, history);

            if (contentChanged || keepAliveDue)
            {
                foreach (var lcd in group)
                {
                    await RunSafeAsync(lcd, (d, ct) => d.PushFrameAsync(frame, ct), cancellationToken);
                }
            }

            if (_previewRequested && png is null)
            {
                png = renderer.EncodePng();
            }
        }

        if (contentChanged || keepAliveDue)
        {
            state.Settings = settings;
            state.TelemetryTimestamp = snapshot.Timestamp;
            state.LastPushAt = now;
        }

        if (_previewRequested)
        {
            if (png is null)
            {
                var preview = GetRenderer(renderers, PreviewSize, PreviewSize);
                preview.Render(settings, snapshot, history);
                png = preview.EncodePng();
            }

            PreviewRendered?.Invoke(png);
        }
    }

    private void RecordHistory(TelemetrySnapshot snapshot)
    {
        var selector = profiles.Current.Lcd.SensorSelectors.FirstOrDefault();
        if (selector is not null && snapshot.TryResolve(selector, out var reading))
        {
            _history.Record(selector, reading.Value);
        }
    }

    private static LcdFrameRenderer GetRenderer(Dictionary<(int Width, int Height), LcdFrameRenderer> renderers, int width, int height)
    {
        if (!renderers.TryGetValue((width, height), out var renderer))
        {
            renderer = new LcdFrameRenderer(width, height);
            renderers[(width, height)] = renderer;
        }

        return renderer;
    }

    private async Task RunSafeAsync(IAioLcdDevice lcd, Func<IAioLcdDevice, CancellationToken, ValueTask> action, CancellationToken cancellationToken)
    {
        try
        {
            await action(lcd, cancellationToken);
            if (_failingDevices.Remove(lcd.Id))
            {
                LogRecovered(lcd.Id);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            if (_failingDevices.Add(lcd.Id))
            {
                LogFailed(ex, lcd.Id);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "LCD update for {DeviceId} failed; retrying silently")]
    private partial void LogFailed(Exception exception, string deviceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "LCD updates for {DeviceId} work again")]
    private partial void LogRecovered(string deviceId);

    private sealed class LoopState
    {
        public IReadOnlyList<IHardwareDevice>? Devices { get; init; }

        public int? Brightness { get; set; }

        public bool BuiltInActive { get; set; }

        public LcdSettings? Settings { get; set; }

        public DateTimeOffset TelemetryTimestamp { get; set; }

        public DateTimeOffset LastPushAt { get; set; }
    }
}
