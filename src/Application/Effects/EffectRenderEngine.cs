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
    ILogger<EffectRenderEngine> logger)
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(250);

    private readonly Dictionary<string, RgbFrameDispatcher> _dispatchers = new(StringComparer.Ordinal);
    private readonly ManualResetEventSlim _wakeSignal = new(false);
    private volatile IReadOnlyList<ZonePreview> _latestPreview = [];
    private volatile CanvasFrame? _latestCanvas;
    private volatile bool _previewRequested;
    private volatile bool _wakeOnTelemetry;

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

                var effectChanged = frame.SyncEffect(profile.Effect, registry, frameStart);
                _wakeOnTelemetry = frame.Effect is { Descriptor: { UsesSensor: true, IsAnimated: false } };
                var layoutChanged = frame.SyncLayout(profile, currentDevices);
                if (layoutChanged)
                {
                    SyncDispatchers(frame.Plans, settingsOptions.KeepAliveInterval);
                }

                if (frame.Plans.Count == 0 && !previewRequested)
                {
                    staticRendered = false;
                    staticPreviewBuilt = false;
                    lastStaticTelemetryTimestamp = null;
                    FramesPerSecond = 0;
                    _wakeSignal.Wait(IdleDelay, cancellationToken);
                    continue;
                }

                var descriptor = frame.Effect!.Descriptor;
                if (!descriptor.IsAnimated)
                {
                    var telemetryChanged = descriptor.UsesSensor && snapshot.Timestamp != lastStaticTelemetryTimestamp;
                    var stateChanged = !staticRendered || effectChanged || layoutChanged || telemetryChanged;
                    if (stateChanged || (previewRequested && !staticPreviewBuilt))
                    {
                        var staticEffectTime = (frameStart - frame.EffectStartedAt).TotalSeconds;
                        var staticPixelsReady = RenderFrame(frame, profile.Effect, staticEffectTime, snapshot);
                        staticRendered = true;
                        lastStaticTelemetryTimestamp = descriptor.UsesSensor ? snapshot.Timestamp : null;
                        if (previewRequested)
                        {
                            _latestPreview = BuildPreview(frame.Plans);
                            PublishCanvas(frame, profile.Effect, staticEffectTime, snapshot, staticPixelsReady);
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
                    _wakeSignal.Wait(cancellationToken);
                    continue;
                }

                staticRendered = false;
                staticPreviewBuilt = false;
                lastStaticTelemetryTimestamp = null;

                var effectTime = (frameStart - frame.EffectStartedAt).TotalSeconds;
                var pixelsReady = RenderFrame(frame, profile.Effect, effectTime, snapshot);

                if (previewRequested && frameStart - lastPreview >= previewInterval)
                {
                    _latestPreview = BuildPreview(frame.Plans);
                    lastPreview = frameStart;
                }

                if (previewRequested && (!canvasPublished || frameStart - lastCanvas >= canvasInterval))
                {
                    PublishCanvas(frame, profile.Effect, effectTime, snapshot, pixelsReady);
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

                var targetFps = Math.Clamp(profile.Effect.TargetFps, 1, settingsOptions.MaxFramesPerSecond);
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
    private bool RenderFrame(FrameState frame, EffectSettings settings, double effectTime, TelemetrySnapshot snapshot)
    {
        var context = new EffectFrameContext(
            frame.Width, frame.Height, effectTime, settings, frame.Colors, snapshot,
            frame.Speed, frame.Angle, frame.SensorSelector, frame.TemperatureLow, frame.TemperatureHigh);
        var brightness = Math.Clamp(settings.Brightness, 0f, 1f);
        var pixelsReady = false;
        try
        {
            // Device frames stay on direct sampling. The 320x180 buffer is filled only for effects that
            // cannot sample points, or later for the UI canvas while a preview subscriber is connected.
            var sampledDirectly = false;
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
            foreach (var plan in frame.Plans)
            {
                Array.Fill(plan.Colors, RgbColor.Black);
            }
        }

        foreach (var plan in frame.Plans)
        {
            if (_dispatchers.TryGetValue(plan.Device.Id, out var dispatcher))
            {
                dispatcher.Post(plan.Colors);
            }
        }

        return pixelsReady;
    }

    private void PublishCanvas(FrameState frame, EffectSettings settings, double effectTime, TelemetrySnapshot snapshot, bool pixelsReady)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Pixels.Length < frame.Width * frame.Height * 4)
        {
            return;
        }

        if (!pixelsReady)
        {
            var context = new EffectFrameContext(
                frame.Width, frame.Height, effectTime, settings, frame.Colors, snapshot,
                frame.Speed, frame.Angle, frame.SensorSelector, frame.TemperatureLow, frame.TemperatureHigh);
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

        public bool EffectFailed { get; set; }

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

            return true;
        }

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
