using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using RigForge.Application.AioDisplay;
using RigForge.Application.Diagnostics;
using RigForge.Application.Cooling;
using RigForge.Application.Devices;
using RigForge.Application.Effects;
using RigForge.Application.Profiles;
using RigForge.Application.Telemetry;
using RigForge.Core.Models;
using RigForge.Host.Daemon.Contracts;
using RigForge.Host.Daemon.Hubs;

namespace RigForge.Host.Daemon.Services;

/// <summary>
/// Pushes engine output to connected UI clients. Nothing is serialized or sent while no client is connected,
/// and high-rate previews only go to clients that asked for them.
/// </summary>
internal sealed partial class UiBroadcastService(
    IHubContext<ControlHub, IControlClient> hub,
    ClientRegistry clients,
    SystemStateBuilder state,
    TelemetryService telemetry,
    FanControlEngine fans,
    EffectRenderEngine effects,
    LcdRenderEngine lcd,
    ProfileService profiles,
    DeviceManager devices,
    PerformanceMonitor performance,
    ILogger<UiBroadcastService> logger) : BackgroundService
{
    private static readonly TimeSpan LedPreviewInterval = TimeSpan.FromMilliseconds(50);

    private readonly Channel<bool> _subscriptionSignal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        telemetry.Updated += OnTelemetry;
        fans.StatusUpdated += OnFanStatus;
        lcd.PreviewRendered += OnLcdPreview;
        profiles.Changed += OnProfileChanged;
        devices.DevicesChanged += OnStateChanged;
        clients.SubscriptionsChanged += OnSubscriptionsChanged;

        try
        {
            IReadOnlyList<ZonePreview>? lastPreview = null;
            CanvasFrame? lastCanvas = null;
            while (!stoppingToken.IsCancellationRequested)
            {
                while (!clients.HasSubscribers(PreviewChannel.Leds))
                {
                    lastPreview = null;
                    lastCanvas = null;
                    while (_subscriptionSignal.Reader.TryRead(out _))
                    {
                    }

                    if (clients.HasSubscribers(PreviewChannel.Leds))
                    {
                        break;
                    }

                    await _subscriptionSignal.Reader.WaitToReadAsync(stoppingToken);
                }

                using var timer = new PeriodicTimer(LedPreviewInterval);
                while (clients.HasSubscribers(PreviewChannel.Leds) && await timer.WaitForNextTickAsync(stoppingToken))
                {
                    if (!clients.HasSubscribers(PreviewChannel.Leds))
                    {
                        continue;
                    }

                    var preview = effects.LatestPreview;
                    if (preview.Count > 0 && !ReferenceEquals(preview, lastPreview))
                    {
                        lastPreview = preview;
                        await SendAsync(hub.Clients.Group(ClientRegistry.GroupName(PreviewChannel.Leds)).LedPreview(DtoMapper.ToDto(preview)));
                    }

                    var canvas = effects.LatestCanvas;
                    if (canvas is { Jpeg.Length: > 0 } && !ReferenceEquals(canvas, lastCanvas))
                    {
                        lastCanvas = canvas;
                        await SendAsync(hub.Clients.Group(ClientRegistry.GroupName(PreviewChannel.Leds)).CanvasPreview(Convert.ToBase64String(canvas.Jpeg)));
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            telemetry.Updated -= OnTelemetry;
            fans.StatusUpdated -= OnFanStatus;
            lcd.PreviewRendered -= OnLcdPreview;
            profiles.Changed -= OnProfileChanged;
            devices.DevicesChanged -= OnStateChanged;
            clients.SubscriptionsChanged -= OnSubscriptionsChanged;
        }
    }

    private void OnSubscriptionsChanged() => _subscriptionSignal.Writer.TryWrite(true);

    private void OnTelemetry(TelemetrySnapshot snapshot)
    {
        if (clients.ClientCount > 0)
        {
            _ = SendAsync(hub.Clients.All.Telemetry(DtoMapper.ToDto(snapshot)));
            _ = SendAsync(hub.Clients.All.Performance(DtoMapper.ToDto(performance.Capture())));
        }
    }

    private void OnFanStatus(IReadOnlyList<FanChannelStatus> status)
    {
        if (clients.ClientCount > 0)
        {
            _ = SendAsync(hub.Clients.All.FanStatus(status.Select(DtoMapper.ToDto).ToArray()));
        }
    }

    private void OnLcdPreview(byte[] png) =>
        _ = SendAsync(hub.Clients.Group(ClientRegistry.GroupName(PreviewChannel.Lcd)).LcdPreview(Convert.ToBase64String(png)));

    private void OnProfileChanged(RigProfile profile) => OnStateChanged();

    private void OnStateChanged()
    {
        if (clients.ClientCount <= 0)
        {
            return;
        }

        if (clients.ActiveMutationConnectionId is { } callerId)
        {
            if (clients.ClientCount > 1)
            {
                _ = SendAsync(hub.Clients.AllExcept(callerId).StateChanged(state.Build()));
            }

            return;
        }

        _ = SendAsync(hub.Clients.All.StateChanged(state.Build()));
    }

    private async Task SendAsync(Task send)
    {
        try
        {
            await send;
        }
        catch (Exception ex)
        {
            LogSendFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Broadcasting to UI clients failed")]
    private partial void LogSendFailed(Exception exception);
}
