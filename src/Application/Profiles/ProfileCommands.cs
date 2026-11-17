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

    /// <summary>Adds the correction or replaces the one for the same device.</summary>
    public static Task<ProfileUpdateResult> UpsertColorCorrectionAsync(this ProfileService profiles, DeviceColorCorrection correction, CancellationToken cancellationToken) =>
        profiles.UpdateAsync(
            p => p with { ColorCorrections = [.. p.ColorCorrections.Where(c => c.DeviceId != correction.DeviceId), correction] },
            cancellationToken);

    public static Task<ProfileUpdateResult> RemoveColorCorrectionAsync(this ProfileService profiles, string deviceId, CancellationToken cancellationToken) =>
        profiles.UpdateAsync(
            p => p with { ColorCorrections = p.ColorCorrections.Where(c => c.DeviceId != deviceId).ToArray() },
            cancellationToken);

    /// <summary>Adds or replaces the LCD settings override for one device. <see cref="RigProfile.Lcd"/> stays the fallback for everyone else.</summary>
    public static Task<ProfileUpdateResult> UpsertDisplayAsync(this ProfileService profiles, string deviceId, LcdSettings settings, CancellationToken cancellationToken) =>
        profiles.UpdateAsync(current =>
        {
            var displays = new Dictionary<string, LcdSettings>(StringComparer.Ordinal);
            if (current.Displays is not null)
            {
                foreach (var (key, value) in current.Displays)
                {
                    displays[key] = value;
                }
            }

            displays[deviceId] = settings;
            return current with { Displays = displays };
        }, cancellationToken);

    /// <summary>
    /// Adds the palette at the end, or replaces the one with the same id in place.
    /// The name is trimmed and each parseable color is stored as uppercase #RRGGBB.
    /// </summary>
    public static Task<ProfileUpdateResult> UpsertColorPaletteAsync(this ProfileService profiles, ColorPalette palette, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(palette);
        var normalized = NormalizeColorPalette(palette);
        return profiles.UpdateAsync(current =>
        {
            var palettes = current.ColorPalettes ?? [];
            var next = new List<ColorPalette>(palettes.Count + 1);
            var replaced = false;
            foreach (var existing in palettes)
            {
                if (existing is not null && string.Equals(existing.Id, normalized.Id, StringComparison.Ordinal))
                {
                    next.Add(normalized);
                    replaced = true;
                }
                else if (existing is not null)
                {
                    next.Add(existing);
                }
            }

            if (!replaced)
            {
                next.Add(normalized);
            }

            return current with { ColorPalettes = next };
        }, cancellationToken);
    }

    public static Task<ProfileUpdateResult> RemoveColorPaletteAsync(this ProfileService profiles, string paletteId, CancellationToken cancellationToken) =>
        profiles.UpdateAsync(current =>
        {
            var palettes = current.ColorPalettes ?? [];
            return current with
            {
                ColorPalettes = palettes
                    .Where(palette => palette is not null && !string.Equals(palette.Id, paletteId, StringComparison.Ordinal))
                    .ToArray(),
            };
        }, cancellationToken);

    private static ColorPalette NormalizeColorPalette(ColorPalette palette)
    {
        var colors = new List<string>();
        if (palette.Colors is not null)
        {
            foreach (var color in palette.Colors)
            {
                colors.Add(RgbColor.TryParse(color, out var parsed) ? parsed.ToString() : color ?? "");
            }
        }

        return palette with
        {
            Name = palette.Name?.Trim() ?? "",
            Colors = colors,
        };
    }

    /// <summary>Removes a per-device LCD override so the device falls back to <see cref="RigProfile.Lcd"/>.</summary>
    public static Task<ProfileUpdateResult> RemoveDisplayAsync(this ProfileService profiles, string deviceId, CancellationToken cancellationToken) =>
        profiles.UpdateAsync(current =>
        {
            var displays = new Dictionary<string, LcdSettings>(StringComparer.Ordinal);
            if (current.Displays is not null)
            {
                foreach (var (key, value) in current.Displays)
                {
                    if (!string.Equals(key, deviceId, StringComparison.Ordinal))
                    {
                        displays[key] = value;
                    }
                }
            }

            return current with { Displays = displays };
        }, cancellationToken);
}
