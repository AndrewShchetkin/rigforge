using System.Text.RegularExpressions;
using HidSharp;
using Microsoft.Extensions.Logging;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Asus;
using RigForge.Infrastructure.CrossPlatform.AsRock;
using RigForge.Infrastructure.CrossPlatform.CoolerMaster;
using RigForge.Infrastructure.CrossPlatform.Corsair;
using RigForge.Infrastructure.CrossPlatform.Gigabyte;
using RigForge.Infrastructure.CrossPlatform.Hid;
using RigForge.Infrastructure.CrossPlatform.LianLi;
using RigForge.Infrastructure.CrossPlatform.Msi;
using RigForge.Infrastructure.CrossPlatform.Nzxt;
using RigForge.Infrastructure.CrossPlatform.Peripherals.Logitech;
using RigForge.Infrastructure.CrossPlatform.Peripherals.QmkVial;
using RigForge.Infrastructure.CrossPlatform.Peripherals.Razer;

namespace RigForge.Infrastructure.CrossPlatform.Discovery;

/// <summary>
/// Stage 5 discovery: vendor HID collections that look like RGB controllers, but whose protocol
/// is not implemented, are reported as <see cref="DeviceSupportStatus.Unmanaged"/> instead of disappearing.
/// Standard keyboard, mouse, audio, and consumer interfaces are ignored. One physical device yields one entry.
/// </summary>
internal sealed partial class UnmanagedRgbControllerProbe(
    ILogger<UnmanagedRgbControllerProbe> logger,
    IDeviceManifestCatalog? manifests = null) : IDeviceProbe
{
    public const ushort VendorUsagePageFloor = 0xFF00;

    /// <summary>Vendor HID collections shorter than this are consumer/audio endpoints, not RGB controllers.</summary>
    public const int MinimumControllerReportLength = 8;

    public static readonly IReadOnlySet<int> LightingVendorIds = new HashSet<int>
    {
        0x1E71, // NZXT
        0x0B05, // ASUS
        0x1B1C, // Corsair
        0x1462, // MSI
        0x0DB0, // MSI
        0x048D, // ITE / Gigabyte
        0x26CE, // ASRock
        0x0CF2, // Lian Li
        0x2516, // Cooler Master
        0x1532, // Razer
        0x046D, // Logitech
        0x1038, // SteelSeries
        0x0951, // HyperX
        0x1E7D, // Roccat
        0x28DE, // Valve
        0x3434, // Keychron
    };

    public string Name => "Unmanaged RGB controllers";

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken) =>
        ProbeAsync([], cancellationToken);

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(
        IReadOnlyList<IHardwareDevice> existingDevices,
        CancellationToken cancellationToken)
    {
        var devices = new List<IHardwareDevice>();
        List<HidDevice> found;
        try
        {
            found = DeviceList.Local.GetHidDevices()
                .Where(static device => LightingVendorIds.Contains(device.VendorID))
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogEnumerationFailed(ex);
            return Task.FromResult<IReadOnlyList<IHardwareDevice>>(devices);
        }

        var eligible = new List<DiscoveredUnmanagedInterface>(found.Count);
        foreach (var hid in found)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var usage = FirstVendorUsage(hid);
            if (usage is null)
            {
                continue;
            }

            var output = OutputLength(hid);
            var feature = FeatureLength(hid);
            var productName = hid.SafeProductName();
            if (!IsUnmanagedCandidate(hid.VendorID, hid.ProductID, usage.Value, output, feature, manifests, productName))
            {
                continue;
            }

            eligible.Add(new DiscoveredUnmanagedInterface(
                hid,
                usage.Value,
                output,
                feature,
                productName,
                hid.SafeSerialNumber()));
        }

        var grouped = CollapseCore(
            eligible,
            static item => item.Device.VendorID,
            static item => item.Device.ProductID,
            static item => item.Serial,
            static item => item.Device.DevicePath,
            static item => item.ProductName,
            static item => item.OutputReportLength,
            static item => item.FeatureReportLength);

        foreach (var item in grouped)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hid = item.Device;
            var serial = item.Serial;
            var name = item.ProductName;
            var instanceKey = UnmanagedInstanceKey(serial, hid.DevicePath, name);
            var id = $"unmanaged:{hid.VendorID:x4}:{hid.ProductID:x4}:{instanceKey}";
            if (existingDevices.FirstOrDefault(existing => existing.Id == id) is { } kept)
            {
                devices.Add(kept);
                continue;
            }

            var vendor = hid.SafeManufacturer();
            if (vendor.Length == 0)
            {
                vendor = VendorName(hid.VendorID);
            }

            devices.Add(new UnmanagedRgbControllerDevice(
                hid.VendorID,
                hid.ProductID,
                name,
                vendor,
                hid.DevicePath,
                "USB HID",
                $"0x{item.Usage >> 16:X4}",
                instanceKey.Length > 0 ? instanceKey : null));
            LogUnmanaged(vendor, name.Length > 0 ? name : id, hid.VendorID, hid.ProductID);
        }

        return Task.FromResult<IReadOnlyList<IHardwareDevice>>(devices);
    }

    /// <summary>
    /// True for a vendor-specific HID collection with an output or feature report of at least
    /// <see cref="MinimumControllerReportLength"/> bytes that is not already owned by a direct protocol
    /// or a manifest. Onboard USB audio mute/jack endpoints are ignored.
    /// </summary>
    public static bool IsUnmanagedCandidate(
        int vendorId,
        int productId,
        uint packedUsage,
        int outputReportLength,
        int featureReportLength,
        IDeviceManifestCatalog? manifests,
        string? productName = null)
    {
        if (!LightingVendorIds.Contains(vendorId))
        {
            return false;
        }

        var page = (ushort)(packedUsage >> 16);
        var usage = (ushort)packedUsage;
        if (page < VendorUsagePageFloor)
        {
            return false;
        }

        if (Math.Max(outputReportLength, featureReportLength) < MinimumControllerReportLength)
        {
            return false;
        }

        if (IsUsbAudioProduct(productName))
        {
            return false;
        }

        if (page == QmkVialProtocol.UsagePage && usage == QmkVialProtocol.Usage)
        {
            return false;
        }

        return !IsKnownSupported(vendorId, productId, manifests);
    }

    /// <summary>
    /// Groups one physical device's HID collections. A serial number wins; otherwise the key is the
    /// device path with <c>&amp;col01</c>/<c>&amp;col02</c> and the <c>#…</c> collection suffix removed,
    /// or the product name when the path is empty.
    /// </summary>
    public static string UnmanagedInstanceKey(string? serial, string? devicePath, string? productName)
    {
        var serialNumber = serial?.Trim() ?? string.Empty;
        return serialNumber.Length > 0
            ? serialNumber
            : PhysicalContainerOrProductKey(devicePath, productName);
    }

    /// <summary>
    /// Path identity shared by every HID collection of one interface, or the product name when the path is empty.
    /// </summary>
    public static string PhysicalContainerOrProductKey(string? devicePath, string? productName)
    {
        var container = HidCollectionContainer(devicePath);
        if (container.Length > 0)
        {
            return container;
        }

        return productName?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Collapses multi-collection HID interfaces to the collection with the widest output or feature report.
    /// Collections below <see cref="MinimumControllerReportLength"/> bytes are dropped.
    /// </summary>
    public static IReadOnlyList<UnmanagedInterfaceCandidate> CollapseCollections(
        IEnumerable<UnmanagedInterfaceCandidate> candidates) =>
        CollapseCore(
            candidates,
            static candidate => candidate.VendorId,
            static candidate => candidate.ProductId,
            static candidate => candidate.Serial,
            static candidate => candidate.DevicePath,
            static candidate => candidate.ProductName,
            static candidate => candidate.OutputReportLength,
            static candidate => candidate.FeatureReportLength);

    /// <summary>One HID collection considered for unmanaged RGB discovery.</summary>
    public readonly record struct UnmanagedInterfaceCandidate(
        int VendorId,
        int ProductId,
        string Serial,
        string DevicePath,
        string ProductName,
        int OutputReportLength,
        int FeatureReportLength);

    public static bool IsKnownSupported(int vendorId, int productId, IDeviceManifestCatalog? manifests)
    {
        if (manifests?.FindUsb(vendorId, productId) is not null)
        {
            return true;
        }

        if (vendorId == LogitechHidPpProtocol.VendorId && LogitechHidPpProtocol.ProductIds.Contains(productId))
        {
            return true;
        }

        if (vendorId == RazerChromaProtocol.VendorId && RazerChromaProtocol.ProductIds.Contains(productId))
        {
            return true;
        }

        if (vendorId == CoolerMasterArgbProtocol.VendorId && CoolerMasterArgbProtocol.ProductIds.Contains(productId))
        {
            return true;
        }

        if (vendorId == LianLiUniHubProtocol.VendorId && LianLiUniHubProtocol.ProductIds.Contains(productId))
        {
            return true;
        }

        if (vendorId == GigabyteFusion2Protocol.VendorId && GigabyteFusion2Protocol.ProductIds.Contains(productId))
        {
            return true;
        }

        if ((vendorId == MsiMysticLightProtocol.VendorId || vendorId == MsiMysticLightProtocol.AlternateVendorId)
            && MsiMysticLightProtocol.ProductIds.Contains(productId))
        {
            return true;
        }

        if (vendorId == AsRockPolychromeProtocol.VendorId && productId == AsRockPolychromeProtocol.ProductId)
        {
            return true;
        }

        if (vendorId == AuraUsbProtocol.VendorId && AuraUsbProtocol.MainboardProductIds.Contains(productId))
        {
            return true;
        }

        if (vendorId == CorsairProtocol.VendorId
            && (CorsairProtocol.LightingNodeProductIds.Contains(productId) || CorsairProtocol.CommanderCoreProductIds.Contains(productId)))
        {
            return true;
        }

        if (vendorId == NzxtCatalog.VendorId
            && (NzxtCatalog.Krakens.ContainsKey(productId)
                || NzxtCatalog.KrakenX3Devices.ContainsKey(productId)
                || NzxtCatalog.Hue2Devices.ContainsKey(productId)))
        {
            return true;
        }

        return false;
    }

    private static uint? FirstVendorUsage(HidDevice device)
    {
        var output = OutputLength(device);
        var feature = FeatureLength(device);
        if (Math.Max(output, feature) < MinimumControllerReportLength)
        {
            return null;
        }

        foreach (var packed in device.SafeTopLevelUsages())
        {
            if ((packed >> 16) >= VendorUsagePageFloor)
            {
                return packed;
            }
        }

        return null;
    }

    private static int OutputLength(HidDevice device) => SafeLength(device.GetMaxOutputReportLength);

    private static int FeatureLength(HidDevice device) => SafeLength(device.GetMaxFeatureReportLength);

    private static int SafeLength(Func<int> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static IReadOnlyList<T> CollapseCore<T>(
        IEnumerable<T> candidates,
        Func<T, int> vendorId,
        Func<T, int> productId,
        Func<T, string> serial,
        Func<T, string> devicePath,
        Func<T, string> productName,
        Func<T, int> outputReportLength,
        Func<T, int> featureReportLength) =>
        candidates
            .Where(item => Math.Max(outputReportLength(item), featureReportLength(item)) >= MinimumControllerReportLength)
            .GroupBy(item =>
            {
                var rawSerial = serial(item);
                var serialNumber = string.IsNullOrWhiteSpace(rawSerial) ? string.Empty : rawSerial.Trim();
                return (
                    vendorId(item),
                    productId(item),
                    Key: serialNumber.Length > 0
                        ? serialNumber
                        : PhysicalContainerOrProductKey(devicePath(item), productName(item)));
            })
            .Select(group => group
                .OrderByDescending(item => Math.Max(outputReportLength(item), featureReportLength(item)))
                .ThenByDescending(outputReportLength)
                .ThenByDescending(featureReportLength)
                .First())
            .ToArray();

    private static string HidCollectionContainer(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return string.Empty;
        }

        var path = HidCollectionIdPattern().Replace(devicePath.Trim(), string.Empty);
        var firstHash = path.IndexOf('#');
        if (firstHash >= 0)
        {
            var secondHash = path.IndexOf('#', firstHash + 1);
            if (secondHash >= 0)
            {
                path = path[..secondHash];
            }
        }

        path = path.Trim();
        return path.Length == 0 ? string.Empty : path.ToLowerInvariant();
    }

    private static bool IsUsbAudioProduct(string? productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            return false;
        }

        var name = productName.Trim();
        return name.Equals("USB Audio", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Generic USB Audio", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("&col[0-9a-f]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HidCollectionIdPattern();

    private readonly record struct DiscoveredUnmanagedInterface(
        HidDevice Device,
        uint Usage,
        int OutputReportLength,
        int FeatureReportLength,
        string ProductName,
        string Serial);

    private static string VendorName(int vendorId) => vendorId switch
    {
        0x1038 => "SteelSeries",
        0x0951 => "HyperX",
        0x1E7D => "Roccat",
        0x28DE => "Valve",
        0x3434 => "Keychron",
        0x1E71 => "NZXT",
        0x0B05 => "ASUS",
        0x1B1C => "Corsair",
        0x1462 or 0x0DB0 => "MSI",
        0x048D => "Gigabyte",
        0x26CE => "ASRock",
        0x0CF2 => "Lian Li",
        0x2516 => "Cooler Master",
        0x1532 => "Razer",
        0x046D => "Logitech",
        _ => "Unknown",
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Unmanaged RGB controller {Vendor} {Name} ({VendorId:X4}:{ProductId:X4})")]
    private partial void LogUnmanaged(string vendor, string name, int vendorId, int productId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Enumerating unmanaged RGB HID interfaces failed")]
    private partial void LogEnumerationFailed(Exception exception);
}
