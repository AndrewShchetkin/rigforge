using HidSharp;

namespace RigForge.Infrastructure.CrossPlatform.Hid;

public static class HidDeviceExtensions
{
    /// <summary>Serial number, or an empty string when the device does not expose (or refuses to report) one.</summary>
    public static string SafeSerialNumber(this HidDevice device) => Safe(device.GetSerialNumber);

    public static string StableInstanceKey(this HidDevice device) => NormalizeSerialOrHash(device.SafeSerialNumber(), device.DevicePath);

    public static string NormalizeSerialOrHash(string? serial, string devicePath)
    {
        var trimmed = serial?.Trim() ?? string.Empty;
        if (trimmed.Length > 0 && !IsPlaceholderSerial(trimmed))
        {
            return trimmed;
        }

        var hash = 2166136261u;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes((devicePath ?? string.Empty).ToLowerInvariant()))
        {
            hash ^= b;
            hash *= 16777619u;
        }

        return $"path-{hash:x8}";
    }

    private static bool IsPlaceholderSerial(string serial) =>
        serial.Equals("AURA LED Controller", StringComparison.OrdinalIgnoreCase)
        || serial.Equals("000000000000", StringComparison.Ordinal)
        || serial.Equals("0", StringComparison.Ordinal);

    public static string SafeProductName(this HidDevice device) => Safe(device.GetProductName);

    public static string SafeManufacturer(this HidDevice device) => Safe(device.GetManufacturer);

    /// <summary>Usage pages/usages of the top-level collections, as (page &lt;&lt; 16 | usage) values.</summary>
    public static IReadOnlyList<uint> SafeTopLevelUsages(this HidDevice device)
    {
        try
        {
            return device.GetReportDescriptor().DeviceItems.SelectMany(item => item.Usages.GetAllValues()).Distinct().ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static bool HasUsagePage(this HidDevice device, ushort usagePage) =>
        device.SafeTopLevelUsages().Any(usage => usage >> 16 == usagePage);

    private static string Safe(Func<string> read)
    {
        try
        {
            return read()?.Trim() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
