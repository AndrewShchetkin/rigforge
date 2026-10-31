using RigForge.Application.Devices;
using RigForge.Application.Profiles;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Application.Layout;

/// <summary>
/// Combines the saved placements with automatic placements for zones the user has not positioned yet,
/// and applies user-defined LED counts to resizable zones.
/// </summary>
public sealed class CanvasLayoutService(DeviceManager devices, ProfileService profiles)
{
    public VirtualCanvas GetEffectiveCanvas() =>
        BuildEffectiveCanvas(profiles.Current.Canvas, devices.RgbDevices.ToList());

    public static VirtualCanvas BuildEffectiveCanvas(VirtualCanvas canvas, IReadOnlyList<IRgbDevice> rgbDevices)
    {
        var placed = canvas.Placements.Select(p => (p.DeviceId, p.ZoneId)).ToHashSet();
        var unplaced = rgbDevices
            .SelectMany(device => device.Zones.Select(zone => (Device: device, Zone: zone)))
            .Where(item => !placed.Contains((item.Device.Id, item.Zone.Id)))
            .ToList();

        if (unplaced.Count == 0)
        {
            return canvas;
        }

        var columns = (int)Math.Ceiling(Math.Sqrt(unplaced.Count));
        var rows = (int)Math.Ceiling(unplaced.Count / (double)columns);
        var cellWidth = canvas.Width / (float)columns;
        var cellHeight = canvas.Height / (float)rows;

        var placements = new List<ZonePlacement>(canvas.Placements);
        for (var i = 0; i < unplaced.Count; i++)
        {
            var (device, zone) = unplaced[i];
            var (width, height) = zone.Topology == LedTopology.Linear
                ? (cellWidth * 0.8f, Math.Min(cellHeight * 0.8f, Math.Max(6f, cellHeight * 0.2f)))
                : (Math.Min(cellWidth, cellHeight) * 0.8f, Math.Min(cellWidth, cellHeight) * 0.8f);

            if (zone.Topology == LedTopology.Composite)
            {
                width = cellWidth * 0.9f;
                height = Math.Min(cellHeight * 0.8f, width / 3f);
            }

            placements.Add(new ZonePlacement
            {
                DeviceId = device.Id,
                ZoneId = zone.Id,
                X = (i % columns) * cellWidth + (cellWidth - width) / 2f,
                Y = (i / columns) * cellHeight + (cellHeight - height) / 2f,
                Width = width,
                Height = height,
                LedCount = zone.IsResizable ? zone.LedCount : null,
            });
        }

        return canvas with { Placements = placements };
    }

    /// <summary>Pushes LED count overrides from the saved layout into resizable zones (ARGB headers).</summary>
    public void ApplyZoneSizes()
    {
        var overrides = profiles.Current.Canvas.Placements
            .Where(p => p.LedCount is > 0)
            .GroupBy(p => (p.DeviceId, p.ZoneId))
            .ToDictionary(g => g.Key, g => g.First().LedCount!.Value);

        foreach (var device in devices.RgbDevices)
        {
            foreach (var zone in device.Zones)
            {
                if (zone.IsResizable
                    && overrides.TryGetValue((device.Id, zone.Id), out var count)
                    && count != zone.LedCount)
                {
                    device.TryResizeZone(zone.Id, count);
                }
            }
        }
    }
}
