using System.Threading.Channels;
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
    private readonly Channel<bool> _wakeSignal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private volatile bool _previewRequested;

    /// <summary>Raised with a PNG of the latest frame while <see cref="PreviewRequested"/> is set.</summary>
    public event Action<byte[]>? PreviewRendered;

    public bool PreviewRequested
    {
        get => _previewRequested;
        set
        {
            var previous = _previewRequested;
            _previewRequested = value;
            if (previous != value)
            {
                SignalWake();
            }
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        void OnProfileChanged(RigProfile _) => SignalWake();
        void OnDevicesChanged() => SignalWake();
        void OnTelemetryUpdated(TelemetrySnapshot snapshot)
        {
            RecordHistory(snapshot);
            SignalWake();
        }

        profiles.Changed += OnProfileChanged;
        devices.DevicesChanged += OnDevicesChanged;
        telemetry.Updated += OnTelemetryUpdated;

        var renderers = new Dictionary<(int Width, int Height), LcdFrameRenderer>();
        var state = new LoopState();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                DrainWakeSignals();
                var settings = profiles.Current.Lcd;
                var lcds = devices.LcdDevices.ToList();

                if (lcds.Count == 0 && !_previewRequested)
                {
                    DisposeRenderers(renderers);
                    state = new LoopState();
                    await WaitForWakeAsync(cancellationToken);
                    continue;
                }

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
            profiles.Changed -= OnProfileChanged;
            devices.DevicesChanged -= OnDevicesChanged;
            telemetry.Updated -= OnTelemetryUpdated;
            DisposeRenderers(renderers);

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
        var previewRequested = _previewRequested;
        var previewJustEnabled = previewRequested && !state.PreviewActive;
        var contentChanged = !ReferenceEquals(settings, state.Settings) || snapshot.Timestamp != state.TelemetryTimestamp;

        PruneUnusedRenderers(renderers, lcds, settings.Mode, previewRequested);

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

            if (previewRequested && (contentChanged || previewJustEnabled))
            {
                var preview = GetRenderer(renderers, PreviewSize, PreviewSize);
                preview.Render(settings, snapshot, []);
                PreviewRendered?.Invoke(preview.EncodePng());
            }

            state.Settings = settings;
            state.TelemetryTimestamp = snapshot.Timestamp;
            state.PreviewActive = previewRequested;
            return;
        }

        state.BuiltInActive = false;
        var keepAliveDue = now - state.LastPushAt >= options.Value.KeepAliveInterval;
        if (!contentChanged && !keepAliveDue && !previewJustEnabled)
        {
            state.PreviewActive = previewRequested;
            return;
        }

        float[]? history = null;
        byte[]? png = null;

        foreach (var group in lcds.GroupBy(l => (l.Lcd.Width, l.Lcd.Height)))
        {
            var renderer = GetRenderer(renderers, group.Key.Width, group.Key.Height);
            var frame = contentChanged || !renderer.HasRenderedFrame
                ? renderer.Render(settings, snapshot, history ??= _history.Snapshot())
                : renderer.LastFrame;

            if (contentChanged || keepAliveDue)
            {
                foreach (var lcd in group)
                {
                    await RunSafeAsync(lcd, (d, ct) => d.PushFrameAsync(frame, ct), cancellationToken);
                }
            }

            if (previewRequested && (contentChanged || previewJustEnabled) && png is null)
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

        if (previewRequested && (contentChanged || previewJustEnabled))
        {
            if (png is null)
            {
                var preview = GetRenderer(renderers, PreviewSize, PreviewSize);
                if (contentChanged || !preview.HasRenderedFrame)
                {
                    preview.Render(settings, snapshot, history ??= _history.Snapshot());
                }

                png = preview.EncodePng();
            }

            PreviewRendered?.Invoke(png);
        }

        state.PreviewActive = previewRequested;
    }

    private void SignalWake() => _wakeSignal.Writer.TryWrite(true);

    private void DrainWakeSignals()
    {
        while (_wakeSignal.Reader.TryRead(out _))
        {
        }
    }

    private async Task WaitForWakeAsync(CancellationToken cancellationToken)
    {
        if (await _wakeSignal.Reader.WaitToReadAsync(cancellationToken))
        {
            _wakeSignal.Reader.TryRead(out _);
        }
    }

    private static void DisposeRenderers(Dictionary<(int Width, int Height), LcdFrameRenderer> renderers)
    {
        foreach (var renderer in renderers.Values)
        {
            renderer.Dispose();
        }

        renderers.Clear();
    }

    private static void PruneUnusedRenderers(
        Dictionary<(int Width, int Height), LcdFrameRenderer> renderers,
        List<IAioLcdDevice> lcds,
        LcdMode mode,
        bool previewRequested)
    {
        if (renderers.Count == 0)
        {
            return;
        }

        foreach (var key in renderers.Keys.ToArray())
        {
            var needed = mode == LcdMode.BuiltInLiquid
                ? previewRequested && key == (PreviewSize, PreviewSize)
                : lcds.Count > 0
                    ? lcds.Any(l => l.Lcd.Width == key.Width && l.Lcd.Height == key.Height)
                    : previewRequested && key == (PreviewSize, PreviewSize);

            if (!needed && renderers.Remove(key, out var unused))
            {
                unused.Dispose();
            }
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

        public bool PreviewActive { get; set; }

        public LcdSettings? Settings { get; set; }

        public DateTimeOffset TelemetryTimestamp { get; set; }

        public DateTimeOffset LastPushAt { get; set; }
    }
}
