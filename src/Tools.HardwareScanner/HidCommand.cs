using System.Globalization;
using HidSharp;
using RigForge.Infrastructure.CrossPlatform.Asus;
using RigForge.Infrastructure.CrossPlatform.Hid;
using RigForge.Infrastructure.CrossPlatform.Manifests;
using RigForge.Infrastructure.CrossPlatform.Nzxt;

namespace RigForge.Tools.HardwareScanner;

internal static class HidCommand
{
    private static readonly JsonDeviceManifestCatalog Catalog = new(string.Empty);

    private static readonly HashSet<int> KnownVendors = Catalog.All.Select(static manifest => manifest.VendorId).ToHashSet();

    public static int Run(string? vendorFilter, bool allVendors)
    {
        int? vendorId = null;
        if (vendorFilter is not null)
        {
            if (!int.TryParse(vendorFilter.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
            {
                Console.Error.WriteLine($"'{vendorFilter}' is not a hexadecimal vendor id.");
                return 1;
            }

            vendorId = parsed;
        }

        var devices = DeviceList.Local.GetHidDevices()
            .Where(d => vendorId is { } v ? d.VendorID == v : allVendors || KnownVendors.Contains(d.VendorID))
            .OrderBy(d => d.VendorID).ThenBy(d => d.ProductID).ThenBy(d => d.DevicePath, StringComparer.Ordinal)
            .ToList();

        foreach (var device in devices)
        {
            var usages = device.SafeTopLevelUsages();
            Console.WriteLine($"{device.VendorID:X4}:{device.ProductID:X4}  {device.SafeManufacturer()} {device.SafeProductName()}".TrimEnd());
            Console.WriteLine($"    support : {Describe(device)}");
            Console.WriteLine($"    serial  : {Fallback(device.SafeSerialNumber())}");
            Console.WriteLine($"    reports : in {device.GetMaxInputReportLength()} / out {device.GetMaxOutputReportLength()} / feature {device.GetMaxFeatureReportLength()} bytes");
            Console.WriteLine($"    usages  : {(usages.Count == 0 ? "-" : string.Join(", ", usages.Select(u => $"page 0x{u >> 16:X4} usage 0x{u & 0xFFFF:X4}")))}");
            Console.WriteLine($"    path    : {device.DevicePath}");
            Console.WriteLine();
        }

        Console.WriteLine($"{devices.Count} HID interface(s) listed.");
        return 0;
    }

    internal static string Describe(HidDevice device)
    {
        if (Catalog.FindUsb(device.VendorID, device.ProductID) is { } manifest)
        {
            var screen = manifest.Display is { } display ? $", {display.Width}x{display.Height} LCD" : string.Empty;
            return $"supported: {manifest.Name} ({manifest.Protocol}{screen})";
        }

        if (device.VendorID == NzxtCatalog.VendorId)
        {
            return "NZXT device not supported yet; please share this output";
        }

        if (device.VendorID == AuraUsbProtocol.VendorId)
        {
            return "ASUS device, not an Aura mainboard controller (add its PID to Hardware:Asus:ExtraProductIds if it is one)";
        }

        return KnownVendors.Contains(device.VendorID)
            ? "known RGB vendor, product not in the built-in manifest"
            : "-";
    }

    private static string Fallback(string value) => string.IsNullOrEmpty(value) ? "-" : value;
}
