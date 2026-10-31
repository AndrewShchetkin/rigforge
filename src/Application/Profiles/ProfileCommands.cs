using RigForge.Core.Models;

namespace RigForge.Application.Profiles;

/// <summary>Use cases that modify the profile; shared by the SignalR hub and the REST API.</summary>
public static class ProfileCommands
{
    public static Task<ProfileUpdateResult> UpdateCanvasAsync(this ProfileService profiles, VirtualCanvas canvas, CancellationToken cancellationToken) =>
        profiles.UpdateAsync(p => p with { Canvas = canvas }, cancellationToken);

    public static Task<ProfileUpdateResult> UpdateEffectAsync(this ProfileService profiles, EffectSettings effect, CancellationToken cancellationToken) =>
        profiles.UpdateAsync(p => p with { Effect = effect }, cancellationToken);

    public static Task<ProfileUpdateResult> UpdateLcdAsync(this ProfileService profiles, LcdSettings lcd, CancellationToken cancellationToken) =>
        profiles.UpdateAsync(p => p with { Lcd = lcd }, cancellationToken);

    /// <summary>Adds the curve or replaces the one with the same id.</summary>
    public static Task<ProfileUpdateResult> UpsertFanCurveAsync(this ProfileService profiles, FanCurve curve, CancellationToken cancellationToken) =>
        profiles.UpdateAsync(p => p with { FanCurves = [.. p.FanCurves.Where(c => c.Id != curve.Id), curve] }, cancellationToken);

    /// <summary>Removing a curve hands its channel back to firmware/BIOS control on the next cooling tick.</summary>
    public static Task<ProfileUpdateResult> RemoveFanCurveAsync(this ProfileService profiles, string curveId, CancellationToken cancellationToken) =>
        profiles.UpdateAsync(p => p with { FanCurves = p.FanCurves.Where(c => c.Id != curveId).ToArray() }, cancellationToken);
}
