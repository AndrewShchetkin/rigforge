using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;
using RigForge.Infrastructure.CrossPlatform;

namespace RigForge.Infrastructure.Linux.Hardware;

internal sealed class SysfsHwmonProbe(IOptions<HardwareOptions> options) : IDeviceProbe
{
    private const string HwmonRoot = "/sys/class/hwmon";

    public string Name => "Linux hwmon (sysfs)";

    public async Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(
        IReadOnlyList<IHardwareDevice> existingDevices,
        CancellationToken cancellationToken)
    {
        var discovered = await ProbeAsync(cancellationToken);
        if (existingDevices.Count == 0)
        {
            return discovered;
        }

        var previous = new Dictionary<string, IHardwareDevice>(existingDevices.Count, StringComparer.Ordinal);
        foreach (var device in existingDevices)
        {
            previous.TryAdd(device.Id, device);
        }

        var reused = new List<IHardwareDevice>(discovered.Count);
        foreach (var device in discovered)
        {
            if (previous.TryGetValue(device.Id, out var kept))
            {
                reused.Add(kept);
                if (!ReferenceEquals(kept, device))
                {
                    await device.DisposeAsync();
                }
            }
            else
            {
                reused.Add(device);
            }
        }

        return reused;
    }

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken)
    {
        var devices = new List<IHardwareDevice> { new LinuxSystemDevice() };
        if (!Directory.Exists(HwmonRoot))
        {
            return Task.FromResult<IReadOnlyList<IHardwareDevice>>(devices);
        }

        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateDirectories(HwmonRoot).Order(StringComparer.Ordinal))
        {
            var name = ReadName(path);
            var id = StableId(path, name);
            for (var suffix = 2; !usedIds.Add(id); suffix++)
            {
                id = $"{StableId(path, name)}-{suffix}";
            }

            var chip = new SysfsHwmonChip(path, name, id, options.Value.DryRun);
            if (chip.HasSensors || chip.Channels.Count > 0)
            {
                devices.Add(chip);
            }
        }

        return Task.FromResult<IReadOnlyList<IHardwareDevice>>(devices);
    }

    private static string ReadName(string path)
    {
        try
        {
            return File.ReadAllText(Path.Combine(path, "name")).Trim();
        }
        catch (IOException)
        {
            return Path.GetFileName(path);
        }
    }

    /// <summary>hwmonN numbers change between boots, the underlying device path does not.</summary>
    private static string StableId(string path, string name)
    {
        try
        {
            var target = new DirectoryInfo(Path.Combine(path, "device")).ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null)
            {
                return $"hwmon:{name}:{target.Name}";
            }
        }
        catch (IOException)
        {
        }

        return $"hwmon:{name}:{Path.GetFileName(path)}";
    }
}
