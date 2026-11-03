using RigForge.Application.Devices;
using RigForge.Application.Profiles;
using RigForge.Application.Telemetry;
using RigForge.Core.Models;
using RigForge.Host.Daemon.Contracts;
using RigForge.Host.Daemon.Services;

namespace RigForge.Host.Daemon.Api;

/// <summary>
/// REST surface for scripts, diagnostics and Postman. The UI uses the SignalR hub; both share the same
/// validated profile commands. Validation failures return RFC 7807 problem details (400).
/// </summary>
public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapRigForgeApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").WithTags("RigForge");

        api.MapGet("/health", (SystemStateBuilder state) => state.BuildInfo())
            .WithName("GetHealth")
            .WithSummary("Daemon version, uptime and memory usage.");

        api.MapGet("/state", (SystemStateBuilder state) => state.Build())
            .WithName("GetState")
            .WithSummary("Full snapshot: devices, profile, effective layout, effects, telemetry, fan status, warnings.");

        api.MapGet("/devices", (DeviceManager devices) => devices.Devices.Select(DtoMapper.ToDto).ToArray())
            .WithName("GetDevices")
            .WithSummary("Detected devices with zones, fan channels and screens.");

        api.MapPost("/devices/rescan", async (DeviceManager devices, CancellationToken cancellationToken) =>
            {
                await devices.ScanAsync(cancellationToken);
                return devices.Devices.Select(DtoMapper.ToDto).ToArray();
            })
            .WithName("RescanDevices")
            .WithSummary("Releases all devices and runs detection again.");

        api.MapGet("/telemetry", (TelemetryService telemetry) => DtoMapper.ToDto(telemetry.Latest))
            .WithName("GetTelemetry")
            .WithSummary("Latest value of every sensor.");

        api.MapGet("/profile", (ProfileService profiles) => profiles.Current)
            .WithName("GetProfile")
            .WithSummary("The active profile (layout, effect, fan curves, LCD).");

        api.MapPut("/profile/effect", (EffectSettings effect, ProfileService profiles, CancellationToken ct) =>
                ToResult(profiles.UpdateEffectAsync(effect, ct)))
            .WithName("UpdateEffect")
            .WithSummary("Replaces the lighting effect settings.");

        api.MapPut("/profile/canvas", (VirtualCanvas canvas, ProfileService profiles, CancellationToken ct) =>
                ToResult(profiles.UpdateCanvasAsync(canvas, ct)))
            .WithName("UpdateCanvas")
            .WithSummary("Replaces the spatial layout (all zone placements).");

        api.MapPut("/profile/lcd", (LcdSettings lcd, ProfileService profiles, CancellationToken ct) =>
                ToResult(profiles.UpdateLcdAsync(lcd, ct)))
            .WithName("UpdateLcd")
            .WithSummary("Replaces the AIO screen settings.");

        api.MapPut("/profile/fan-curves/{id}", (string id, FanCurve curve, ProfileService profiles, CancellationToken ct) =>
                ToResult(profiles.UpsertFanCurveAsync(curve with { Id = id }, ct)))
            .WithName("UpsertFanCurve")
            .WithSummary("Creates or replaces a fan curve.");

        api.MapDelete("/profile/fan-curves/{id}", (string id, ProfileService profiles, CancellationToken ct) =>
                ToResult(profiles.RemoveFanCurveAsync(id, ct)))
            .WithName("RemoveFanCurve")
            .WithSummary("Removes a fan curve; the channel returns to firmware/BIOS control.");

        api.MapPut("/profile/color-corrections/{deviceId}", (string deviceId, DeviceColorCorrection correction, ProfileService profiles, CancellationToken ct) =>
                ToResult(profiles.UpsertColorCorrectionAsync(correction with { DeviceId = deviceId }, ct)))
            .WithName("UpsertColorCorrection")
            .WithSummary("Creates or replaces the gamma and white balance of one device.");

        api.MapDelete("/profile/color-corrections/{deviceId}", (string deviceId, ProfileService profiles, CancellationToken ct) =>
                ToResult(profiles.RemoveColorCorrectionAsync(deviceId, ct)))
            .WithName("RemoveColorCorrection")
            .WithSummary("Removes a device color correction; the device returns to neutral gamma and white balance.");

        return app;
    }

    private static async Task<IResult> ToResult(Task<ProfileUpdateResult> update)
    {
        var result = await update;
        return result.IsValid
            ? Results.Ok(result.Profile)
            : Results.ValidationProblem(
                result.Errors.GroupBy(e => e.Split(':', 2)[0]).ToDictionary(g => g.Key, g => g.Select(e => e.Split(':', 2)[^1].Trim()).ToArray()),
                title: "The profile change is invalid.");
    }
}
