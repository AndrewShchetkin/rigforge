using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Application.Devices;
using RigForge.Application.Layout;
using RigForge.Application.Profiles;
using RigForge.Application.Telemetry;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using SkiaSharp;

namespace RigForge.Application.Effects;

/// <summary>LED colors of one zone for the UI preview (RGB triplets).</summary>
public sealed record ZonePreview(string DeviceId, string ZoneId, byte[] Rgb);

/// <summary>JPEG of the virtual canvas while a UI client is watching the lighting preview.</summary>
public sealed record CanvasFrame(int Width, int Height, byte[] Jpeg);

/// <summary>What the lighting loop is doing. <see cref="EffectRenderMode.Sleeping"/> and <see cref="EffectRenderMode.Blackout"/> wait on a signal and use no CPU.</summary>
public enum EffectRenderMode
{
    Sleeping,
    Blackout,
    DirectSampling,
    CanvasRaster,
}

/// <summary>
/// The lighting loop: samples the active effect at every LED and hands the frames to per-device
/// dispatchers. While a UI client watches the lighting preview it also paints the virtual canvas.
/// Runs on a dedicated thread paced by <see cref="IPrecisionTimer"/> so 60 FPS is reachable on Windows.
/// </summary>
public sealed partial class EffectRenderEngine(
    DeviceManager devices,
    ProfileService profiles,
    TelemetryService telemetry,
    EffectRegistry registry,
    IPrecisionTimer precisionTimer,
    IOptions<RenderingOptions> options,
    ILogger<EffectRenderEngine> logger,
    IAudioLevelProvider? audio = null)
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan DefaultIdentifyDuration = TimeSpan.FromSeconds(2);
    private static readonly RgbColor IdentifyLit = new(0x00, 0xF5, 0xFF);
    private static readonly RgbColor IdentifyDim = new(0x0A, 0x10, 0x18);

    private readonly Dictionary<string, RgbFrameDispatcher> _dispatchers = new(StringComparer.Ordinal);
    private readonly ManualResetEventSlim _wakeSignal = new(false);
    private readonly Lock _identifyGate = new();
    private volatile IReadOnlyList<ZonePreview> _latestPreview = [];
    private volatile CanvasFrame? _latestCanvas;
    private volatile bool _previewRequested;
    private volatile bool _wakeOnTelemetry;
    private string? _identifyDeviceId;
    private string? _identifyZoneId;
    private long _identifyStartedAt;
    private long _identifyExpiresAt;
    private double _lastFrameMicroseconds;
    private double _averageFrameMicroseconds;
    private int _renderMode = (int)EffectRenderMode.Sleeping;
    private int _activeRgbDeviceCount;
    private int _totalControlledLeds;

    /// <summary>Set by the host while at least one UI client watches the LED preview.</summary>
    public bool PreviewRequested
    {
        get => _previewRequested;
        set
        {
            var previous = _previewRequested;
            _previewRequested = value;
            if (!previous && value)
            {
                _wakeSignal.Set();
            }
        }
    }

    public IReadOnlyList<ZonePreview> LatestPreview => _latestPreview;

    public CanvasFrame? LatestCanvas => _latestCanvas;

    public double FramesPerSecond { get; private set; }

    /// <summary>Microseconds spent inside the last <c>RenderFrame</c>. Written by the render thread.</summary>
    public double LastFrameMicroseconds => Volatile.Read(ref _lastFrameMicroseconds);

    /// <summary>Smoothed frame time (85% previous, 15% latest). Zero while lighting is powered off.</summary>
    public double AverageFrameMicroseconds => Volatile.Read(ref _averageFrameMicroseconds);

    public EffectRenderMode CurrentRenderMode => (EffectRenderMode)Volatile.Read(ref _renderMode);

    public int ActiveRgbDeviceCount => Volatile.Read(ref _activeRgbDeviceCount);

    public int TotalControlledLeds => Volatile.Read(ref _totalControlledLeds);

    /// <summary>
    /// Blinks <paramref name="deviceId"/> (or one of its zones) cyan and white at 4 Hz so the user can find it.
    /// The loop keeps animating for <paramref name="duration"/> even when the effect is static or lighting is off.
    /// </summary>
    public void TriggerIdentify(string deviceId, string? zoneId = null, TimeSpan? duration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        var length = duration ?? DefaultIdentifyDuration;
        if (length < TimeSpan.Zero)
        {
            length = TimeSpan.Zero;
        }

        var started = Stopwatch.GetTimestamp();
        var ticks = (long)Math.Round(length.TotalSeconds * Stopwatch.Frequency);
        if (ticks < 0)
        {
            ticks = 0;
        }

        lock (_identifyGate)
        {
            _identifyDeviceId = deviceId;
            _identifyZoneId = string.IsNullOrEmpty(zoneId) ? null : zoneId;
            _identifyStartedAt = started;
            _identifyExpiresAt = ticks > long.MaxValue - started ? long.MaxValue : started + ticks;
        }

        _wakeSignal.Set();
    }

    public Task RunAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                RenderLoop(cancellationToken);
                completion.SetResult();
            }
            catch (OperationCanceledException)
            {
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "RigForge effect renderer",
            Priority = ThreadPriority.AboveNormal,
        };

        thread.Start();
        return completion.Task;
    }

    private void RenderLoop(CancellationToken cancellationToken)
    {
        var settingsOptions = options.Value;
        var clock = Stopwatch.StartNew();
        var frame = new FrameState();
        var identifyWasActive = false;

        void OnProfileChanged(RigProfile _) => _wakeSignal.Set();
        void OnDevicesChanged() => _wakeSignal.Set();
        void OnTelemetryUpdated(TelemetrySnapshot _)
        {
            if (_wakeOnTelemetry)
            {
                _wakeSignal.Set();
            }
        }

        profiles.Changed += OnProfileChanged;
        devices.DevicesChanged += OnDevicesChanged;
        telemetry.Updated += OnTelemetryUpdated;

        try
        {
            var fpsWindowStart = clock.Elapsed;
            var framesInWindow = 0;
            var lastPreview = TimeSpan.Zero;
            var lastCanvas = TimeSpan.Zero;
            var canvasPublished = false;
            var previewInterval = TimeSpan.FromSeconds(1.0 / Math.Max(1, settingsOptions.PreviewFramesPerSecond));
            var canvasInterval = TimeSpan.FromSeconds(1.0 / Math.Max(1, settingsOptions.CanvasPreviewFramesPerSecond));
            var staticRendered = false;
            var staticPreviewBuilt = false;
            var blackoutApplied = false;
            var blackoutPreviewPublished = false;
            DateTimeOffset? lastStaticTelemetryTimestamp = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                _wakeSignal.Reset();
                var frameStart = clock.Elapsed;
                var profile = profiles.Current;
                var currentDevices = devices.Devices;
                var previewRequested = _previewRequested;
                if (!previewRequested)
                {
                    canvasPublished = false;
                }

                var snapshot = telemetry.Latest;
                var effectSettings = profile.Effect;

                var effectChanged = frame.SyncEffect(effectSettings, registry, frameStart);
                var layoutChanged = frame.SyncLayout(profile, currentDevices);
                if (layoutChanged)
                {
                    SyncDispatchers(frame.Plans, settingsOptions.KeepAliveInterval);
                }

                PublishLoad(frame);

                var identifyActive = TryGetIdentify(out var identify);
                if (identifyWasActive && !identifyActive)
                {
                    staticRendered = false;
                    staticPreviewBuilt = false;
                    blackoutApplied = false;
                    blackoutPreviewPublished = false;
                }

                identifyWasActive = identifyActive;
                _wakeOnTelemetry = frame.Effect is { Descriptor: { UsesSensor: true, IsAnimated: false } }
                    && effectSettings.Enabled
                    && !identifyActive;

                if (!effectSettings.Enabled && !identifyActive)
                {
                    staticRendered = false;
                    staticPreviewBuilt = false;
                    lastStaticTelemetryTimestamp = null;
                    if (!blackoutApplied || layoutChanged || effectChanged || (previewRequested && !blackoutPreviewPublished))
                    {
                        DriveBlack(frame, previewRequested);
                        blackoutApplied = true;
                        blackoutPreviewPublished = previewRequested;
                    }

                    FramesPerSecond = 0;
                    framesInWindow = 0;
                    fpsWindowStart = clock.Elapsed;
                    SetAverageFrameMicroseconds(0);
                    SetRenderMode(EffectRenderMode.Blackout);
                    _wakeSignal.Wait(cancellationToken);
                    continue;
                }

                blackoutApplied = false;
                blackoutPreviewPublished = false;

                if (frame.Plans.Count == 0 && !previewRequested)
                {
                    staticRendered = false;
                    staticPreviewBuilt = false;
                    lastStaticTelemetryTimestamp = null;
                    FramesPerSecond = 0;
                    SetRenderMode(EffectRenderMode.Sleeping);
                    _wakeSignal.Wait(IdleDelay, cancellationToken);
                    continue;
                }

                var descriptor = frame.Effect!.Descriptor;
                if (!descriptor.IsAnimated && !identifyActive)
                {
                    var telemetryChanged = descriptor.UsesSensor && snapshot.Timestamp != lastStaticTelemetryTimestamp;
                    var stateChanged = !staticRendered || effectChanged || layoutChanged || telemetryChanged;
                    if (stateChanged || (previewRequested && !staticPreviewBuilt))
                    {
                        ReadAudio(frame, out var staticLevel, out var staticBands);
                        var staticEffectTime = (frameStart - frame.EffectStartedAt).TotalSeconds;
                        var staticPixelsReady = RenderFrame(frame, effectSettings, staticEffectTime, snapshot, staticLevel, staticBands, identify: null);
                        staticRendered = true;
                        lastStaticTelemetryTimestamp = descriptor.UsesSensor ? snapshot.Timestamp : null;
                        if (previewRequested)
                        {
                            _latestPreview = BuildPreview(frame.Plans);
                            PublishCanvas(frame, effectSettings, staticEffectTime, snapshot, staticPixelsReady, staticLevel, staticBands);
                            lastPreview = frameStart;
                            staticPreviewBuilt = true;
                        }
                        else
                        {
                            staticPreviewBuilt = false;
                        }
                    }

                    FramesPerSecond = 0;
                    framesInWindow = 0;
                    fpsWindowStart = clock.Elapsed;
                    SetRenderMode(EffectRenderMode.Sleeping);
                    _wakeSignal.Wait(cancellationToken);
                    continue;
                }

                staticRendered = false;
                staticPreviewBuilt = false;
                lastStaticTelemetryTimestamp = null;

                ReadAudio(frame, out var audioLevel, out var audioBands);
                var effectTime = (frameStart - frame.EffectStartedAt).TotalSeconds;
                var pixelsReady = RenderFrame(
                    frame,
                    effectSettings,
                    effectTime,
                    snapshot,
                    audioLevel,
                    audioBands,
                    identifyActive ? identify : null);

                if (previewRequested && (identifyActive || frameStart - lastPreview >= previewInterval))
                {
                    _latestPreview = BuildPreview(frame.Plans);
                    lastPreview = frameStart;
                }

                if (previewRequested && (!canvasPublished || frameStart - lastCanvas >= canvasInterval))
                {
                    PublishCanvas(frame, effectSettings, effectTime, snapshot, pixelsReady, audioLevel, audioBands);
                    lastCanvas = frameStart;
                    canvasPublished = true;
                }

                framesInWindow++;
                if (frameStart - fpsWindowStart >= TimeSpan.FromSeconds(1))
                {
                    FramesPerSecond = framesInWindow / (frameStart - fpsWindowStart).TotalSeconds;
                    fpsWindowStart = frameStart;
                    framesInWindow = 0;
                }

                var targetFps = Math.Clamp(effectSettings.TargetFps, 1, settingsOptions.MaxFramesPerSecond);
                var remaining = TimeSpan.FromSeconds(1.0 / targetFps) - (clock.Elapsed - frameStart);
                if (remaining > TimeSpan.Zero)
                {
                    precisionTimer.Sleep(remaining);
                }
            }
        }
        finally
        {
            profiles.Changed -= OnProfileChanged;
            devices.DevicesChanged -= OnDevicesChanged;
            telemetry.Updated -= OnTelemetryUpdated;
            frame.Dispose();
            foreach (var dispatcher in _dispatchers.Values)
            {
                dispatcher.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
            }

            _dispatchers.Clear();
        }
    }

    /// <returns>True when <see cref="FrameState.Pixels"/> already holds this frame. Direct sampling leaves it untouched.</returns>
    private bool RenderFrame(
        FrameState frame,
        EffectSettings settings,
        double effectTime,
        TelemetrySnapshot snapshot,
        float audioLevel,
        ReadOnlyMemory<float> audioBands,
        IdentifyTarget? identify)
    {
        var started = Stopwatch.GetTimestamp();
        var context = frame.CreateContext(effectTime, settings, snapshot, audioLevel, audioBands);
        var brightness = Math.Clamp(settings.Brightness, 0f, 1f);
        var pixelsReady = false;
        var sampledDirectly = false;
        try
        {
            // Device frames stay on direct sampling. The 320x180 buffer is filled only for effects that
            // cannot sample points, or later for the UI canvas while a preview subscriber is connected.
            if (frame.Plans.Count > 0
                && frame.Effect!.TrySamplePoints(frame.Plans[0].Points, frame.Plans[0].Colors, in context))
            {
                sampledDirectly = true;
                for (var i = 1; i < frame.Plans.Count; i++)
                {
                    frame.Effect.TrySamplePoints(frame.Plans[i].Points, frame.Plans[i].Colors, in context);
                }

                foreach (var plan in frame.Plans)
                {
                    plan.ApplyDirectBrightness(brightness);
                }
            }

            if (!sampledDirectly)
            {
                var pixels = frame.Pixels.AsSpan();
                CanvasBuffer.Fill(pixels, RgbColor.Black);
                frame.Effect!.Render(pixels, in context);
                foreach (var plan in frame.Plans)
                {
                    plan.Sample(pixels, frame.Width, frame.Height, brightness);
                }

                pixelsReady = true;
            }

            frame.EffectFailed = false;
        }
        catch (Exception ex)
        {
            // A faulty effect must not take the loop down; show black and log once.
            if (!frame.EffectFailed)
            {
                LogEffectFailed(ex, frame.Effect!.Descriptor.Id);
                frame.EffectFailed = true;
            }

            pixelsReady = false;
            sampledDirectly = false;
            foreach (var plan in frame.Plans)
            {
                Array.Fill(plan.Colors, RgbColor.Black);
            }
        }

        if (identify is not null)
        {
            ApplyIdentify(frame, identify.Value);
        }

        foreach (var plan in frame.Plans)
        {
            if (_dispatchers.TryGetValue(plan.Device.Id, out var dispatcher))
            {
                dispatcher.Post(plan.Colors);
            }
        }

        var elapsedUs = Stopwatch.GetElapsedTime(started).TotalMicroseconds;
        Volatile.Write(ref _lastFrameMicroseconds, elapsedUs);
        var average = Volatile.Read(ref _averageFrameMicroseconds);
        SetAverageFrameMicroseconds(average <= 0d ? elapsedUs : (0.85d * average) + (0.15d * elapsedUs));
        SetRenderMode(sampledDirectly ? EffectRenderMode.DirectSampling : EffectRenderMode.CanvasRaster);
        return pixelsReady;
    }

    private void PublishCanvas(
        FrameState frame,
        EffectSettings settings,
        double effectTime,
        TelemetrySnapshot snapshot,
        bool pixelsReady,
        float audioLevel,
        ReadOnlyMemory<float> audioBands)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Pixels.Length < frame.Width * frame.Height * 4)
        {
            return;
        }

        if (!pixelsReady)
        {
            var context = frame.CreateContext(effectTime, settings, snapshot, audioLevel, audioBands);
            try
            {
                frame.Effect!.Render(frame.Pixels.AsSpan(), in context);
            }
            catch (Exception ex)
            {
                LogCanvasPreviewFailed(ex, frame.Effect!.Descriptor.Id);
                return;
            }
        }

        _latestCanvas = new CanvasFrame(frame.Width, frame.Height, frame.EncodeJpeg());
    }

    private void DriveBlack(FrameState frame, bool previewRequested)
    {
        foreach (var plan in frame.Plans)
        {
            Array.Fill(plan.Colors, RgbColor.Black);
            if (_dispatchers.TryGetValue(plan.Device.Id, out var dispatcher))
            {
                dispatcher.Post(plan.Colors);
            }
        }

        if (!previewRequested)
        {
            return;
        }

        _latestPreview = BuildPreview(frame.Plans);
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Pixels.Length < frame.Width * frame.Height * 4)
        {
            return;
        }

        CanvasBuffer.Fill(frame.Pixels, RgbColor.Black);
        _latestCanvas = new CanvasFrame(frame.Width, frame.Height, frame.EncodeJpeg());
    }

    private void ReadAudio(FrameState frame, out float level, out ReadOnlyMemory<float> bands)
    {
        level = 0f;
        bands = default;
        if (frame.Effect?.Descriptor.UsesAudio == true && audio is { IsSupported: true })
        {
            level = audio.ReadSpectrum(frame.AudioBands);
            bands = frame.AudioBands;
        }
    }

    private bool TryGetIdentify(out IdentifyTarget target)
    {
        lock (_identifyGate)
        {
            if (_identifyDeviceId is null || Stopwatch.GetTimestamp() >= _identifyExpiresAt)
            {
                _identifyDeviceId = null;
                _identifyZoneId = null;
                target = default;
                return false;
            }

            target = new IdentifyTarget(_identifyDeviceId, _identifyZoneId, _identifyStartedAt);
            return true;
        }
    }

    private static void ApplyIdentify(FrameState frame, IdentifyTarget identify)
    {
        var color = IdentifyPhaseOn(identify.StartedAt) ? IdentifyLit : IdentifyDim;
        foreach (var plan in frame.Plans)
        {
            if (!string.Equals(plan.Device.Id, identify.DeviceId, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrEmpty(identify.ZoneId))
            {
                Array.Fill(plan.Colors, color);
                continue;
            }

            var offset = 0;
            foreach (var zone in plan.Zones)
            {
                if (string.Equals(zone.Id, identify.ZoneId, StringComparison.Ordinal))
                {
                    var count = Math.Min(zone.LedCount, plan.Colors.Length - offset);
                    if (count > 0)
                    {
                        Array.Fill(plan.Colors, color, offset, count);
                    }
                }

                offset += zone.LedCount;
            }
        }
    }

    private static bool IdentifyPhaseOn(long startedAt)
    {
        var elapsedTicks = Stopwatch.GetTimestamp() - startedAt;
        if (elapsedTicks < 0)
        {
            elapsedTicks = 0;
        }

        var ticksPerHalf = Stopwatch.Frequency / 8;
        return ticksPerHalf <= 0 || (elapsedTicks / ticksPerHalf & 1) == 0;
    }

    private void PublishLoad(FrameState frame)
    {
        var leds = 0;
        foreach (var plan in frame.Plans)
        {
            leds += plan.Colors.Length;
        }

        Volatile.Write(ref _activeRgbDeviceCount, frame.Plans.Count);
        Volatile.Write(ref _totalControlledLeds, leds);
    }

    private void SetRenderMode(EffectRenderMode mode) => Volatile.Write(ref _renderMode, (int)mode);

    private void SetAverageFrameMicroseconds(double microseconds) => Volatile.Write(ref _averageFrameMicroseconds, microseconds);

    private readonly record struct IdentifyTarget(string DeviceId, string? ZoneId, long StartedAt);

    private void SyncDispatchers(IReadOnlyList<LedSamplePlan> plans, TimeSpan keepAlive)
    {
        var wanted = plans.Select(p => p.Device).ToDictionary(d => d.Id, StringComparer.Ordinal);

        foreach (var id in _dispatchers.Keys.Where(id => !wanted.ContainsKey(id)).ToList())
        {
            _ = _dispatchers[id].DisposeAsync().AsTask();
            _dispatchers.Remove(id);
        }

        foreach (var (id, device) in wanted)
        {
            if (!_dispatchers.ContainsKey(id))
            {
                _dispatchers[id] = new RgbFrameDispatcher(device, keepAlive, logger);
            }
        }
    }

    private static List<ZonePreview> BuildPreview(IReadOnlyList<LedSamplePlan> plans)
    {
        var preview = new List<ZonePreview>();
        foreach (var plan in plans)
        {
            var offset = 0;
            foreach (var zone in plan.Zones)
            {
                var rgb = new byte[zone.LedCount * 3];
                for (var i = 0; i < zone.LedCount; i++)
                {
                    var color = plan.Colors[offset + i];
                    rgb[i * 3] = color.R;
                    rgb[i * 3 + 1] = color.G;
                    rgb[i * 3 + 2] = color.B;
                }

                preview.Add(new ZonePreview(plan.Device.Id, zone.Id, rgb));
                offset += zone.LedCount;
            }
        }

        return preview;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Effect {EffectId} threw while rendering; showing black")]
    private partial void LogEffectFailed(Exception exception, string effectId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Effect {EffectId} threw while painting the canvas preview")]
    private partial void LogCanvasPreviewFailed(Exception exception, string effectId);

    /// <summary>Render-thread-only state: active effect, canvas buffer and sample plans.</summary>
    private sealed class FrameState : IDisposable
    {
        private EffectSettings? _settings;
        private RigProfile? _layoutProfile;
        private IReadOnlyList<IHardwareDevice>? _layoutDevices;

        public IEffect? Effect { get; private set; }

        public TimeSpan EffectStartedAt { get; private set; }

        public IReadOnlyList<RgbColor> Colors { get; private set; } = [];

        public float Speed { get; private set; } = 1f;

        public float Angle { get; private set; }

        public string? SensorSelector { get; private set; }

        public float TemperatureLow { get; private set; } = 35f;

        public float TemperatureHigh { get; private set; } = 80f;

        public IReadOnlyDictionary<string, double> Numbers { get; private set; } = EmptyNumbers;

        public IReadOnlyDictionary<string, bool> Booleans { get; private set; } = EmptyBooleans;

        public IReadOnlyDictionary<string, string> Strings { get; private set; } = EmptyStrings;

        public float[] AudioBands { get; } = new float[16];

        public bool EffectFailed { get; set; }

        private static readonly Dictionary<string, double> EmptyNumbers = new(StringComparer.Ordinal);

        private static readonly Dictionary<string, bool> EmptyBooleans = new(StringComparer.Ordinal);

        private static readonly Dictionary<string, string> EmptyStrings = new(StringComparer.Ordinal);

        public byte[] Pixels { get; private set; } = [];

        public int Width { get; private set; }

        public int Height { get; private set; }

        public List<LedSamplePlan> Plans { get; private set; } = [];

        public bool SyncEffect(EffectSettings settings, EffectRegistry registry, TimeSpan now)
        {
            if (ReferenceEquals(settings, _settings))
            {
                return false;
            }

            _settings = settings;

            if (Effect is null || !string.Equals(Effect.Descriptor.Id, settings.EffectId, StringComparison.OrdinalIgnoreCase))
            {
                Effect?.Dispose();
                Effect = registry.Create(settings.EffectId);
                EffectStartedAt = now;
                EffectFailed = false;
            }

            // Parameter JSON is parsed once per settings change, never per frame or per LED.
            var parsed = EffectParameterValues.Parse(settings, Effect.Descriptor);
            Colors = parsed.Colors;
            Speed = parsed.Speed;
            Angle = parsed.Angle;
            SensorSelector = parsed.SensorSelector;
            TemperatureLow = parsed.TemperatureLow;
            TemperatureHigh = parsed.TemperatureHigh;
            Numbers = parsed.Numbers;
            Booleans = parsed.Booleans;
            Strings = parsed.Strings;

            return true;
        }

        public EffectFrameContext CreateContext(
            double effectTime,
            EffectSettings settings,
            TelemetrySnapshot snapshot,
            float audioLevel,
            ReadOnlyMemory<float> audioBands) =>
            new(
                Width,
                Height,
                effectTime,
                settings,
                Colors,
                snapshot,
                Speed,
                Angle,
                SensorSelector,
                TemperatureLow,
                TemperatureHigh,
                audioLevel,
                audioBands,
                Numbers,
                Booleans,
                Strings);

        /// <summary>Returns true when the plans were rebuilt.</summary>
        public bool SyncLayout(RigProfile profile, IReadOnlyList<IHardwareDevice> devices)
        {
            if (ReferenceEquals(profile, _layoutProfile)
                && ReferenceEquals(devices, _layoutDevices)
                && !Plans.Any(p => p.IsStale))
            {
                return false;
            }

            _layoutProfile = profile;
            _layoutDevices = devices;

            var rgbDevices = devices.OfType<IRgbDevice>().ToList();
            var canvas = CanvasLayoutService.BuildEffectiveCanvas(profile.Canvas, rgbDevices);
            var corrections = (profile.ColorCorrections ?? [])
                .GroupBy(c => c.DeviceId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            Plans = rgbDevices
                .Select(d => LedSamplePlan.Build(
                    d,
                    canvas.Placements,
                    canvas.Width,
                    canvas.Height,
                    corrections.GetValueOrDefault(d.Id)))
                .Where(p => p.Colors.Length > 0)
                .ToList();

            var requiredBytes = canvas.Width * canvas.Height * 4;
            if (Pixels.Length != requiredBytes || Width != canvas.Width || Height != canvas.Height)
            {
                Width = canvas.Width;
                Height = canvas.Height;
                Pixels = new byte[requiredBytes];
            }

            return true;
        }

        public byte[] EncodeJpeg()
        {
            var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using var image = SKImage.FromPixelCopy(info, Pixels);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 80);
            return data.ToArray();
        }

        public void Dispose()
        {
            Effect?.Dispose();
        }
    }
}
