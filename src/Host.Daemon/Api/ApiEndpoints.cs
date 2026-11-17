using System.Net;
using Microsoft.AspNetCore.Mvc;
using RigForge.Application.Devices;
using RigForge.Application.Diagnostics;
using RigForge.Application.Effects;
using RigForge.Application.Profiles;
using RigForge.Application.Telemetry;
using RigForge.Core.Models;
using RigForge.Host.Daemon.Contracts;
using RigForge.Host.Daemon.Hosting;
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

        api.MapGet("/host/autostart", (IAutostartService autostart) => ToAutostartDto(autostart.GetStatus()))
            .WithName("GetAutostart")
            .WithSummary("Whether sign-in autostart is supported and currently enabled.");

        api.MapPut("/host/autostart", async (AutostartUpdateRequest request, IAutostartService autostart, CancellationToken ct) =>
            {
                try
                {
                    var status = await autostart.SetEnabledAsync(request.Enabled, ct);
                    return Results.Ok(ToAutostartDto(status));
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Problem(
                        detail: exception.Message,
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Autostart could not be changed.");
                }
            })
            .WithName("SetAutostart")
            .WithSummary("Enables or disables sign-in autostart without stopping the daemon.");

        api.MapPost("/host/shutdown", RequestShutdown)
            .WithName("ShutdownHost")
            .WithSummary("Stops the daemon after a short delay so fan curves and the AIO screen can be restored.");

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

        api.MapGet("/performance", (PerformanceMonitor monitor) => DtoMapper.ToDto(monitor.Capture()))
            .WithName("GetPerformance")
            .WithSummary("Process CPU, memory, GC, and lighting render telemetry.");

        api.MapPost("/devices/{deviceId}/identify", (string deviceId, [FromQuery] string? zoneId, EffectRenderEngine effects) =>
            {
                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["deviceId"] = ["A device id is required."],
                    });
                }

                effects.TriggerIdentify(deviceId, zoneId);
                return Results.Accepted();
            })
            .WithName("IdentifyDevice")
            .WithSummary("Blinks a device, or one of its zones, so it can be found.");

        api.MapGet("/profile", (ProfileService profiles) => profiles.Current)
            .WithName("GetProfile")
            .WithSummary("The active profile (layout, effect, fan curves, LCD).");

        api.MapPut("/profile", (RigProfile profile, ProfileService profiles, CancellationToken ct) =>
                ToResult(profiles.ReplaceProfileAsync(profile, ct)))
            .WithName("ImportProfile")
            .WithSummary("Replaces the whole profile.");

        api.MapPut("/profile/displays/{deviceId}", (string deviceId, LcdSettings lcd, ProfileService profiles, CancellationToken ct) =>
                ToResult(profiles.UpsertDisplayAsync(deviceId, lcd, ct)))
            .WithName("UpsertDisplay")
            .WithSummary("Creates or replaces the LCD settings for one device.");

        api.MapDelete("/profile/displays/{deviceId}", (string deviceId, ProfileService profiles, CancellationToken ct) =>
                ToResult(profiles.RemoveDisplayAsync(deviceId, ct)))
            .WithName("RemoveDisplay")
            .WithSummary("Removes a per-device LCD override.");

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

        api.MapPut("/profile/color-palettes/{id}", (string id, ColorPalette palette, ProfileService profiles, CancellationToken ct) =>
                ToResult(profiles.UpsertColorPaletteAsync(palette with { Id = id }, ct)))
            .WithName("UpsertColorPalette")
            .WithSummary("Creates or replaces a named color palette. The route id overwrites the body.");

        api.MapDelete("/profile/color-palettes/{id}", (string id, ProfileService profiles, CancellationToken ct) =>
                ToResult(profiles.RemoveColorPaletteAsync(id, ct)))
            .WithName("RemoveColorPalette")
            .WithSummary("Removes a named color palette.");

        return app;
    }

    /// <summary>
    /// Installer shutdown. Browser requests are rejected: a page that can reach loopback must not be able to
    /// stop fan control. The response returns before <see cref="IHostApplicationLifetime.StopApplication"/>
    /// so the cooling loop can hand curves back to firmware.
    /// </summary>
    public static IResult RequestShutdown(HttpContext httpContext, IHostApplicationLifetime lifetime)
    {
        if (IsShutdownForbidden(httpContext))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(75);
            lifetime.StopApplication();
        });

        return Results.Accepted(value: new { stopping = true });
    }

    private static bool IsShutdownForbidden(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.ContainsKey("Origin")
            || httpContext.Request.Headers.ContainsKey("Referer")
            || httpContext.Request.Headers.ContainsKey("Sec-Fetch-Site"))
        {
            return true;
        }

        return httpContext.Connection.RemoteIpAddress is { } address && !IPAddress.IsLoopback(address);
    }

    private static AutostartStatusDto ToAutostartDto(AutostartStatus status) =>
        new(status.Supported, status.Enabled, status.TaskRegistered, status.Details);

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
