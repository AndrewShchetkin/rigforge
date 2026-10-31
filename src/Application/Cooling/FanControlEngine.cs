using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Application.Devices;
using RigForge.Application.Profiles;
using RigForge.Application.Telemetry;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Application.Cooling;

public sealed record FanChannelStatus(
    string CurveId,
    string DeviceId,
    string ChannelId,
    float? Temperature,
    int TargetDuty,
    int AppliedDuty,
    bool Failsafe,
    string? Message);

/// <summary>
/// 1 Hz control loop: resolves each curve's temperature sources, runs its <see cref="FanChannelRegulator"/>
/// and writes the duty to the controller. Channels whose curve disappears, and all channels on shutdown,
/// are handed back to firmware/BIOS control.
/// </summary>
public sealed partial class FanControlEngine(
    DeviceManager devices,
    ProfileService profiles,
    TelemetryService telemetry,
    IOptions<CoolingOptions> options,
    TimeProvider timeProvider,
    ILogger<FanControlEngine> logger)
{
    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromSeconds(5);

    private readonly Dictionary<string, ChannelRuntime> _channels = new(StringComparer.Ordinal);
    private volatile IReadOnlyList<FanChannelStatus> _status = [];

    public event Action<IReadOnlyList<FanChannelStatus>>? StatusUpdated;

    public IReadOnlyList<FanChannelStatus> Status => _status;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval, timeProvider);
        try
        {
            do
            {
                await TickAsync(cancellationToken);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        finally
        {
            await RestoreAllAsync();
        }
    }

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var snapshot = telemetry.Latest;
        var controllers = devices.FanControllers.ToDictionary(c => c.Id, StringComparer.Ordinal);

        var critical = snapshot.TryResolve(TelemetrySnapshot.RoleSelector(SensorRole.LiquidTemperature), out var liquid)
            && now - liquid.Timestamp <= settings.SensorStaleAfter
            && liquid.Value >= settings.CriticalLiquidTemperature;

        var active = new HashSet<string>(StringComparer.Ordinal);
        var status = new List<FanChannelStatus>();

        foreach (var curve in profiles.Current.FanCurves)
        {
            if (!curve.Enabled)
            {
                continue;
            }

            active.Add(curve.Id);
            if (!controllers.TryGetValue(curve.DeviceId, out var controller)
                || controller.Channels.FirstOrDefault(c => c.Id == curve.ChannelId) is not { } channel)
            {
                status.Add(new FanChannelStatus(curve.Id, curve.DeviceId, curve.ChannelId, null, 0, 0, false, "Device or channel not connected"));
                continue;
            }

            if (!_channels.TryGetValue(curve.Id, out var runtime))
            {
                runtime = new ChannelRuntime(curve.DeviceId, curve.ChannelId);
                _channels[curve.Id] = runtime;
            }

            var temperature = ResolveTemperature(curve, snapshot, now, settings.SensorStaleAfter);
            var output = runtime.Regulator.Update(curve, temperature, settings.FailsafeDutyPercent, critical, now);

            var minimum = channel.Kind == FanChannelKind.Pump
                ? Math.Max(channel.MinDutyPercent, settings.MinimumPumpDutyPercent)
                : channel.MinDutyPercent;
            var duty = Math.Clamp((int)MathF.Round(output.Duty), minimum, Math.Max(minimum, channel.MaxDutyPercent));

            string? message = null;
            if (critical)
            {
                message = string.Create(CultureInfo.InvariantCulture, $"Liquid {liquid!.Value:0.0} °C is critical, forcing {settings.FailsafeDutyPercent} %");
            }
            else if (output.Failsafe)
            {
                message = $"No fresh temperature from {string.Join(", ", curve.SensorSelectors)}";
            }

            if (duty != runtime.LastSentDuty || now - runtime.LastSentAt >= settings.RefreshInterval)
            {
                try
                {
                    await controller.SetDutyAsync(channel.Id, duty, cancellationToken);
                    runtime.LastSentDuty = duty;
                    runtime.LastSentAt = now;
                    runtime.LastErrorLogged = false;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    message = $"Write failed: {ex.Message}";
                    if (!runtime.LastErrorLogged)
                    {
                        LogWriteFailed(ex, curve.Id);
                        runtime.LastErrorLogged = true;
                    }
                }
            }

            status.Add(new FanChannelStatus(curve.Id, curve.DeviceId, curve.ChannelId, output.Temperature, (int)MathF.Round(output.TargetDuty), runtime.LastSentDuty ?? duty, output.Failsafe, message));
        }

        foreach (var removed in _channels.Keys.Where(id => !active.Contains(id)).ToList())
        {
            var runtime = _channels[removed];
            _channels.Remove(removed);
            await RestoreAsync(removed, runtime, controllers.GetValueOrDefault(runtime.DeviceId), cancellationToken);
        }

        _status = status;
        StatusUpdated?.Invoke(status);
    }

    private static float? ResolveTemperature(FanCurve curve, TelemetrySnapshot snapshot, DateTimeOffset now, TimeSpan staleAfter)
    {
        float? hottest = null;
        foreach (var selector in curve.SensorSelectors)
        {
            if (snapshot.TryResolve(selector, out var reading)
                && reading.Kind == SensorKind.Temperature
                && now - reading.Timestamp <= staleAfter
                && float.IsFinite(reading.Value))
            {
                hottest = hottest is { } current ? Math.Max(current, reading.Value) : reading.Value;
            }
        }

        return hottest;
    }

    private async Task RestoreAllAsync()
    {
        var controllers = devices.FanControllers.ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (var (curveId, runtime) in _channels)
        {
            using var timeout = new CancellationTokenSource(RestoreTimeout);
            await RestoreAsync(curveId, runtime, controllers.GetValueOrDefault(runtime.DeviceId), timeout.Token);
        }

        _channels.Clear();
    }

    private async Task RestoreAsync(string curveId, ChannelRuntime runtime, IFanController? controller, CancellationToken cancellationToken)
    {
        if (controller is null)
        {
            return;
        }

        try
        {
            await controller.RestoreDefaultAsync(runtime.ChannelId, cancellationToken);
            LogRestored(curveId);
        }
        catch (Exception ex)
        {
            LogRestoreFailed(ex, curveId);
        }
    }

    private sealed class ChannelRuntime(string deviceId, string channelId)
    {
        public string DeviceId { get; } = deviceId;

        public string ChannelId { get; } = channelId;

        public FanChannelRegulator Regulator { get; } = new();

        public int? LastSentDuty { get; set; }

        public DateTimeOffset LastSentAt { get; set; }

        public bool LastErrorLogged { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Setting duty for {CurveId} failed")]
    private partial void LogWriteFailed(Exception exception, string curveId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Channel {CurveId} returned to firmware/BIOS control")]
    private partial void LogRestored(string curveId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not return channel {CurveId} to firmware/BIOS control")]
    private partial void LogRestoreFailed(Exception exception, string curveId);
}
