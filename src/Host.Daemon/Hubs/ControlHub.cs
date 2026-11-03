using Microsoft.AspNetCore.SignalR;
using RigForge.Application.Devices;
using RigForge.Application.Profiles;
using RigForge.Core.Models;
using RigForge.Host.Daemon.Contracts;
using RigForge.Host.Daemon.Services;

namespace RigForge.Host.Daemon.Hubs;

/// <summary>
/// The UI's single entry point. Mutations go through <see cref="ProfileService"/> (validated + persisted) and are
/// broadcast to every client as <see cref="IControlClient.StateChanged"/>; validation errors surface as <see cref="HubException"/>.
/// </summary>
public sealed class ControlHub(
    SystemStateBuilder state,
    ProfileService profiles,
    DeviceManager devices,
    ClientRegistry clients) : Hub<IControlClient>
{
    public override Task OnConnectedAsync()
    {
        clients.Connected(Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        clients.Disconnected(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public SystemStateDto GetState() => state.Build();

    public Task<SystemStateDto> UpdateCanvas(VirtualCanvas canvas) =>
        ApplyAsync(Required(canvas, nameof(canvas)), (p, c, ct) => p.UpdateCanvasAsync(c, ct));

    public Task<SystemStateDto> UpdateEffect(EffectSettings effect) =>
        ApplyAsync(Required(effect, nameof(effect)), (p, e, ct) => p.UpdateEffectAsync(e, ct));

    public Task<SystemStateDto> UpdateLcd(LcdSettings lcd) =>
        ApplyAsync(Required(lcd, nameof(lcd)), (p, l, ct) => p.UpdateLcdAsync(l, ct));

    public Task<SystemStateDto> UpsertFanCurve(FanCurve curve) =>
        ApplyAsync(Required(curve, nameof(curve)), (p, c, ct) => p.UpsertFanCurveAsync(c, ct));

    public Task<SystemStateDto> RemoveFanCurve(string curveId) =>
        ApplyAsync(Required(curveId, nameof(curveId)), (p, id, ct) => p.RemoveFanCurveAsync(id, ct));

    public Task<SystemStateDto> UpsertColorCorrection(DeviceColorCorrection correction) =>
        ApplyAsync(Required(correction, nameof(correction)), (p, c, ct) => p.UpsertColorCorrectionAsync(c, ct));

    public Task<SystemStateDto> RemoveColorCorrection(string deviceId) =>
        ApplyAsync(Required(deviceId, nameof(deviceId)), (p, id, ct) => p.RemoveColorCorrectionAsync(id, ct));

    public async Task<SystemStateDto> RescanDevices()
    {
        using var _ = clients.BeginMutationScope(Context.ConnectionId);
        await devices.ScanAsync(Context.ConnectionAborted);
        return state.Build();
    }

    public async Task SetPreview(PreviewChannel channel, bool enabled)
    {
        var group = ClientRegistry.GroupName(channel);
        if (enabled)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, group);
        }
        else
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
        }

        clients.SetPreview(Context.ConnectionId, channel, enabled);
    }

    private async Task<SystemStateDto> ApplyAsync<T>(T argument, Func<ProfileService, T, CancellationToken, Task<ProfileUpdateResult>> command)
    {
        using var _ = clients.BeginMutationScope(Context.ConnectionId);
        var result = await command(profiles, argument, Context.ConnectionAborted);
        if (!result.IsValid)
        {
            throw new HubException("Validation failed: " + string.Join("; ", result.Errors));
        }

        return state.Build();
    }

    private static T Required<T>(T? value, string name) where T : class =>
        value ?? throw new HubException($"'{name}' is required.");
}
