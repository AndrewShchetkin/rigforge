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
/// Each screen uses <see cref="RigProfile.Displays"/> when that device has an override, otherwise <see cref="RigProfile.Lcd"/>.
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

    private readonly Dictionary<string, SensorHistory> _histories = new(StringComparer.Ordinal);
    private readonly Lock _historyGate = new();
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

    internal static LcdSettings ResolveSettings(RigProfile profile, string deviceId) =>
        profile.Displays is not null && profile.Displays.TryGetValue(deviceId, out var custom)
            ? custom
            : profile.Lcd;

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
                var profile = profiles.Current;
                var lcds = devices.LcdDevices.ToList();

                if (lcds.Count == 0 && !_previewRequested)
                {
                    DisposeRenderers(renderers);
                    state.Reset();
                    state.Devices = null;
                    await WaitForWakeAsync(cancellationToken);
                    continue;
                }

                if (!ReferenceEquals(state.Devices, devices.Devices))
                {
                    state.Reset();
                    state.Devices = devices.Devices;
                }

                await ApplyBrightnessAsync(profile, lcds, state, cancellationToken);
                await RenderAndPushAsync(profile, lcds, renderers, state, cancellationToken);

                var fps = ResolveFrameRate(profile, lcds);
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

    private async Task ApplyBrightnessAsync(
        RigProfile profile,
        List<IAioLcdDevice> lcds,
        LoopState state,
        CancellationToken cancellationToken)
    {
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var lcd in lcds)
        {
            present.Add(lcd.Id);
            var settings = ResolveSettings(profile, lcd.Id);
            if (!state.Brightness.TryGetValue(lcd.Id, out var current) || current != settings.Brightness)
            {
                await RunSafeAsync(lcd, (d, ct) => d.SetBrightnessAsync(settings.Brightness, ct), cancellationToken);
                state.Brightness[lcd.Id] = settings.Brightness;
            }
        }

        foreach (var id in state.Brightness.Keys.Where(id => !present.Contains(id)).ToArray())
        {
            state.Brightness.Remove(id);
            state.BuiltIn.Remove(id);
            state.AppliedSettings.Remove(id);
        }
    }

    private async Task RenderAndPushAsync(
        RigProfile profile,
        List<IAioLcdDevice> lcds,
        Dictionary<(int Width, int Height), LcdFrameRenderer> renderers,
        LoopState state,
        CancellationToken cancellationToken)
    {
        var snapshot = telemetry.Latest;
        var now = timeProvider.GetUtcNow();
        var previewRequested = _previewRequested;
        var previewJustEnabled = previewRequested && !state.PreviewActive;
        var keepAliveDue = now - state.LastPushAt >= options.Value.KeepAliveInterval;
        var contentChanged = snapshot.Timestamp != state.TelemetryTimestamp || !ReferenceEquals(profile.Lcd, state.PreviewSettings);

        foreach (var lcd in lcds)
        {
            var settings = ResolveSettings(profile, lcd.Id);
            if (!state.AppliedSettings.TryGetValue(lcd.Id, out var previous) || !ReferenceEquals(previous, settings))
            {
                contentChanged = true;
            }
        }

        PruneUnusedRenderers(renderers, state, profile, lcds, previewRequested);

        byte[]? png = null;
        var pushed = false;

        foreach (var lcd in lcds)
        {
            var settings = ResolveSettings(profile, lcd.Id);
            var deviceChanged = !state.AppliedSettings.TryGetValue(lcd.Id, out var previous)
                || !ReferenceEquals(previous, settings)
                || snapshot.Timestamp != state.TelemetryTimestamp;

            if (settings.Mode == LcdMode.BuiltInLiquid)
            {
                if (state.BuiltIn.Add(lcd.Id))
                {
                    await RunSafeAsync(lcd, (d, ct) => d.ShowBuiltInScreenAsync(ct), cancellationToken);
                }

                state.AppliedSettings[lcd.Id] = settings;
                continue;
            }

            state.BuiltIn.Remove(lcd.Id);
            if (!deviceChanged && !keepAliveDue)
            {
                continue;
            }

            var key = (lcd.Lcd.Width, lcd.Lcd.Height);
            var renderer = GetRenderer(renderers, key.Width, key.Height);
            var reuse = !deviceChanged
                && renderer.HasRenderedFrame
                && state.RenderedSettings.TryGetValue(key, out var rendered)
                && ReferenceEquals(rendered, settings);
            var frame = reuse
                ? renderer.LastFrame
                : renderer.Render(settings, snapshot, HistoryFor(settings));
            state.RenderedSettings[key] = settings;
            await RunSafeAsync(lcd, (d, ct) => d.PushFrameAsync(frame, ct), cancellationToken);
            pushed = true;
            state.AppliedSettings[lcd.Id] = settings;

            if (previewRequested
                && (contentChanged || previewJustEnabled)
                && png is null
                && ReferenceEquals(settings, profile.Lcd)
                && key == (PreviewSize, PreviewSize))
            {
                png = renderer.EncodePng();
            }
        }

        if (contentChanged || (keepAliveDue && pushed))
        {
            state.PreviewSettings = profile.Lcd;
            state.TelemetryTimestamp = snapshot.Timestamp;
            if (pushed)
            {
                state.LastPushAt = now;
            }
        }

        if (previewRequested && (contentChanged || previewJustEnabled))
        {
            if (png is null)
            {
                var preview = GetRenderer(renderers, PreviewSize, PreviewSize);
                if (contentChanged || !preview.HasRenderedFrame)
                {
                    preview.Render(profile.Lcd, snapshot, HistoryFor(profile.Lcd));
                    state.RenderedSettings[(PreviewSize, PreviewSize)] = profile.Lcd;
                }

                png = preview.EncodePng();
            }

            PreviewRendered?.Invoke(png);
        }

        state.PreviewActive = previewRequested;
    }

    private float ResolveFrameRate(RigProfile profile, List<IAioLcdDevice> lcds)
    {
        var requested = profile.Lcd.FramesPerSecond;
        foreach (var lcd in lcds)
        {
            var settings = ResolveSettings(profile, lcd.Id);
            if (settings.Mode != LcdMode.BuiltInLiquid && settings.FramesPerSecond > requested)
            {
                requested = settings.FramesPerSecond;
            }
        }

        var deviceCap = lcds.Count > 0 ? lcds.Min(static lcd => lcd.Lcd.MaxFramesPerSecond) : options.Value.MaxFramesPerSecond;
        return Math.Clamp(requested, 0.1f, Math.Min(options.Value.MaxFramesPerSecond, deviceCap));
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
        LoopState state,
        RigProfile profile,
        List<IAioLcdDevice> lcds,
        bool previewRequested)
    {
        if (renderers.Count == 0)
        {
            return;
        }

        var needed = new HashSet<(int Width, int Height)>();
        foreach (var lcd in lcds)
        {
            if (ResolveSettings(profile, lcd.Id).Mode != LcdMode.BuiltInLiquid)
            {
                needed.Add((lcd.Lcd.Width, lcd.Lcd.Height));
            }
        }

        if (previewRequested)
        {
            needed.Add((PreviewSize, PreviewSize));
        }

        foreach (var key in renderers.Keys.ToArray())
        {
            if (!needed.Contains(key) && renderers.Remove(key, out var unused))
            {
                unused.Dispose();
                state.RenderedSettings.Remove(key);
            }
        }
    }

    private void RecordHistory(TelemetrySnapshot snapshot)
    {
        var profile = profiles.Current;
        RecordSelector(profile.Lcd, snapshot);
        if (profile.Displays is null)
        {
            return;
        }

        foreach (var settings in profile.Displays.Values)
        {
            RecordSelector(settings, snapshot);
        }
    }

    private void RecordSelector(LcdSettings settings, TelemetrySnapshot snapshot)
    {
        var selector = settings.SensorSelectors.FirstOrDefault();
        if (selector is not null && snapshot.TryResolve(selector, out var reading))
        {
            lock (_historyGate)
            {
                if (!_histories.TryGetValue(selector, out var history))
                {
                    history = new SensorHistory(options.Value.HistorySamples);
                    _histories[selector] = history;
                }

                history.Record(selector, reading.Value);
            }
        }
    }

    private float[] HistoryFor(LcdSettings settings)
    {
        var selector = settings.SensorSelectors.FirstOrDefault();
        if (selector is null)
        {
            return [];
        }

        lock (_historyGate)
        {
            return _histories.TryGetValue(selector, out var history) ? history.Snapshot() : [];
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
        public IReadOnlyList<IHardwareDevice>? Devices { get; set; }

        public Dictionary<string, int> Brightness { get; } = new(StringComparer.Ordinal);

        public HashSet<string> BuiltIn { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, LcdSettings> AppliedSettings { get; } = new(StringComparer.Ordinal);

        public Dictionary<(int Width, int Height), LcdSettings> RenderedSettings { get; } = [];

        public bool PreviewActive { get; set; }

        public LcdSettings? PreviewSettings { get; set; }

        public DateTimeOffset TelemetryTimestamp { get; set; }

        public DateTimeOffset LastPushAt { get; set; }

        public void Reset()
        {
            Brightness.Clear();
            BuiltIn.Clear();
            AppliedSettings.Clear();
            RenderedSettings.Clear();
            PreviewActive = false;
            PreviewSettings = null;
            TelemetryTimestamp = default;
            LastPushAt = default;
        }
    }
}
