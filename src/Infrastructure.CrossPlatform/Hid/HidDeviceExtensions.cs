using HidSharp;

namespace RigForge.Infrastructure.CrossPlatform.Hid;

public static class HidDeviceExtensions
{
    /// <summary>Serial number, or an empty string when the device does not expose (or refuses to report) one.</summary>
    public static string SafeSerialNumber(this HidDevice device) => Safe(device.GetSerialNumber);

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
