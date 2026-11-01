using HidSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Hid;

namespace RigForge.Infrastructure.CrossPlatform.Asus;

internal sealed partial class AsusAuraProbe(
    IOptions<HardwareOptions> options,
    ILogger<AsusAuraProbe> logger,
    IDeviceManifestCatalog? manifests = null) : IDeviceProbe
{
    public string Name => "ASUS Aura USB";

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken) =>
        ProbeAsync([], cancellationToken);

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(
        IReadOnlyList<IHardwareDevice> existingDevices,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Asus.Enabled)
        {
            return Task.FromResult<IReadOnlyList<IHardwareDevice>>([]);
        }

        var productIds = (manifests?.ForProtocol(DeviceProtocolFamily.AsusAuraUsb).Select(m => m.ProductId) ?? AuraUsbProtocol.MainboardProductIds)
            .Concat(settings.Asus.ExtraProductIds)
            .ToHashSet();
        var devices = new List<IHardwareDevice>();

        // The controller exposes several HID collections; only the vendor page 0xFF72 one speaks the Aura protocol.
        var candidates = DeviceList.Local.GetHidDevices(AuraUsbProtocol.VendorId)
            .Where(d => productIds.Contains(d.ProductID) && d.GetMaxOutputReportLength() >= AuraUsbProtocol.ReportLength)
            .Where(d => d.HasUsagePage(AuraUsbProtocol.UsagePage) || d.SafeTopLevelUsages().Count == 0)
            .GroupBy(d => (d.ProductID, Key: d.StableInstanceKey()))
            .Select(g => g.First());

        foreach (var hid in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var serial = hid.SafeSerialNumber();
            var suffix = serial.Length > 0 && !string.Equals(serial, "AURA LED Controller", StringComparison.OrdinalIgnoreCase)
                ? serial
                : hid.StableInstanceKey();
            var id = $"asus-aura:{hid.ProductID:x4}:{suffix}";

            if (existingDevices.FirstOrDefault(e => e.Id == id) is { } existing
                && existing.Properties.TryGetValue("UsbPath", out var usbPath)
                && string.Equals(usbPath, hid.DevicePath, StringComparison.Ordinal))
            {
                devices.Add(existing);
                continue;
            }

            try
            {
                devices.Add(AsusAuraUsbDevice.Open(hid, id, settings.Asus.DefaultArgbLedCount, settings.DryRun));
            }
            catch (Exception ex)
            {
                LogInitFailed(ex, hid.ProductID);
            }
        }

        return Task.FromResult<IReadOnlyList<IHardwareDevice>>(devices);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Initializing the ASUS Aura controller 0x{ProductId:X4} failed; is Armoury Crate's LightingService running?")]
    private partial void LogInitFailed(Exception exception, int productId);
}
