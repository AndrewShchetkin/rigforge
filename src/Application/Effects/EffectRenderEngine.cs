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

/// <summary>
/// The lighting loop: renders the active effect onto the virtual canvas with SkiaSharp, samples it at every
/// LED position and hands the frames to per-device dispatchers. Runs on a dedicated thread paced by
/// <see cref="IPrecisionTimer"/> so 60 FPS is reachable on Windows as well.
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
    private volatile IReadOnlyList<ZonePreview> _latestPreview = [];
    private volatile bool _previewRequested;

    /// <summary>Set by the host while at least one UI client watches the LED preview.</summary>
    public bool PreviewRequested
    {
        get => _previewRequested;
        set => _previewRequested = value;
    }

    public IReadOnlyList<ZonePreview> LatestPreview => _latestPreview;

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

        try
        {
            var fpsWindowStart = clock.Elapsed;
            var framesInWindow = 0;
            var lastPreview = TimeSpan.Zero;
            var previewInterval = TimeSpan.FromSeconds(1.0 / Math.Max(1, settingsOptions.PreviewFramesPerSecond));

            while (!cancellationToken.IsCancellationRequested)
            {
                var frameStart = clock.Elapsed;
                var profile = profiles.Current;

                frame.SyncEffect(profile.Effect, registry, frameStart);
                if (frame.SyncLayout(profile, devices.Devices))
                {
                    SyncDispatchers(frame.Plans, settingsOptions.KeepAliveInterval);
                }

                if (frame.Plans.Count == 0 && !_previewRequested)
                {
                    precisionTimer.Sleep(IdleDelay);
                    continue;
                }

                RenderFrame(frame, profile.Effect, (frameStart - frame.EffectStartedAt).TotalSeconds);

                if (_previewRequested && frameStart - lastPreview >= previewInterval)
                {
                    _latestPreview = BuildPreview(frame.Plans);
                    lastPreview = frameStart;
                }

                framesInWindow++;
                if (frameStart - fpsWindowStart >= TimeSpan.FromSeconds(1))
                {
                    FramesPerSecond = framesInWindow / (frameStart - fpsWindowStart).TotalSeconds;
                    fpsWindowStart = frameStart;
                    framesInWindow = 0;
                }

                var targetFps = frame.Effect!.Descriptor.IsAnimated
                    ? Math.Clamp(profile.Effect.TargetFps, 1, settingsOptions.MaxFramesPerSecond)
                    : settingsOptions.StaticEffectFramesPerSecond;
                var remaining = TimeSpan.FromSeconds(1.0 / targetFps) - (clock.Elapsed - frameStart);
                if (remaining > TimeSpan.Zero)
                {
                    precisionTimer.Sleep(remaining);
                }
            }
        }
        finally
        {
            frame.Dispose();
            foreach (var dispatcher in _dispatchers.Values)
            {
                dispatcher.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
            }

            _dispatchers.Clear();
        }
    }

    private void RenderFrame(FrameState frame, EffectSettings settings, double effectTime)
    {
        var context = new EffectFrameContext(frame.Width, frame.Height, effectTime, settings, frame.Colors, telemetry.Latest);
        try
        {
            frame.Canvas!.Clear(SKColors.Black);
            frame.Effect!.Render(frame.Canvas, context);
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

            frame.Canvas!.Clear(SKColors.Black);
        }

        frame.Canvas.Flush();

        var pixels = frame.Bitmap!.GetPixelSpan();
        var brightness = Math.Clamp(settings.Brightness, 0f, 1f);
        foreach (var plan in frame.Plans)
        {
            plan.Sample(pixels, frame.Width, frame.Height, brightness);
            if (_dispatchers.TryGetValue(plan.Device.Id, out var dispatcher))
            {
                dispatcher.Post(plan.Colors);
            }
        }
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

    /// <summary>Render-thread-only state: active effect, canvas surface and sample plans.</summary>
    private sealed class FrameState : IDisposable
    {
        private EffectSettings? _settings;
        private RigProfile? _layoutProfile;
        private IReadOnlyList<IHardwareDevice>? _layoutDevices;

        public IEffect? Effect { get; private set; }

        public TimeSpan EffectStartedAt { get; private set; }

        public IReadOnlyList<SKColor> Colors { get; private set; } = [];

        public bool EffectFailed { get; set; }

        public SKBitmap? Bitmap { get; private set; }

        public SKCanvas? Canvas { get; private set; }

        public int Width => Bitmap?.Width ?? 0;

        public int Height => Bitmap?.Height ?? 0;

        public List<LedSamplePlan> Plans { get; private set; } = [];

        public void SyncEffect(EffectSettings settings, EffectRegistry registry, TimeSpan now)
        {
            if (ReferenceEquals(settings, _settings))
            {
                return;
            }

            _settings = settings;
            Colors = settings.Colors
                .Select(c => RgbColor.TryParse(c, out var color) ? new SKColor(color.R, color.G, color.B) : SKColors.White)
                .ToArray();

            if (Effect is null || !string.Equals(Effect.Descriptor.Id, settings.EffectId, StringComparison.OrdinalIgnoreCase))
            {
                Effect?.Dispose();
                Effect = registry.Create(settings.EffectId);
                EffectStartedAt = now;
                EffectFailed = false;
            }
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
            var placements = canvas.Placements
                .GroupBy(p => (p.DeviceId, p.ZoneId))
                .ToDictionary(g => g.Key, g => g.First());

            Plans = rgbDevices.Select(d => LedSamplePlan.Build(d, placements)).Where(p => p.Colors.Length > 0).ToList();

            if (Bitmap is null || Bitmap.Width != canvas.Width || Bitmap.Height != canvas.Height)
            {
                Canvas?.Dispose();
                Bitmap?.Dispose();
                Bitmap = new SKBitmap(new SKImageInfo(canvas.Width, canvas.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
                Canvas = new SKCanvas(Bitmap);
            }

            return true;
        }

        public void Dispose()
        {
            Effect?.Dispose();
            Canvas?.Dispose();
            Bitmap?.Dispose();
        }
    }
}
