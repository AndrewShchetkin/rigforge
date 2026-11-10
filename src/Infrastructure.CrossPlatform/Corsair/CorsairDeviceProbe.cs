using HidSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Hid;

namespace RigForge.Infrastructure.CrossPlatform.Corsair;

internal sealed partial class CorsairDeviceProbe(
    IOptions<HardwareOptions> options,
    ILogger<CorsairDeviceProbe> logger,
    IDeviceManifestCatalog? manifests = null) : IDeviceProbe
{
    public string Name => "Corsair Lighting Node / Commander Core";

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken) =>
        ProbeAsync([], cancellationToken);

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(
        IReadOnlyList<IHardwareDevice> existingDevices,
        CancellationToken cancellationToken)
    {
        var dryRun = options.Value.DryRun;
        var vendorIds = new HashSet<int> { CorsairProtocol.VendorId };
        var nodePids = new HashSet<int>(CorsairProtocol.LightingNodeProductIds);
        var corePids = new HashSet<int>(CorsairProtocol.CommanderCoreProductIds);
        if (manifests is not null)
        {
            foreach (var manifest in manifests.ForProtocol(DeviceProtocolFamily.CorsairLightingNode))
            {
                vendorIds.Add(manifest.VendorId);
                nodePids.Add(manifest.ProductId);
            }

            foreach (var manifest in manifests.ForProtocol(DeviceProtocolFamily.CorsairCommanderCore))
            {
                vendorIds.Add(manifest.VendorId);
                corePids.Add(manifest.ProductId);
            }
        }

        var devices = new List<IHardwareDevice>();
        foreach (var vendorId in vendorIds)
        {
            var grouped = DeviceList.Local.GetHidDevices(vendorId)
                .Where(hid => nodePids.Contains(hid.ProductID) || corePids.Contains(hid.ProductID))
                .GroupBy(hid => (hid.ProductID, Key: hid.StableInstanceKey()));

            foreach (var group in grouped)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var node = nodePids.Contains(group.Key.ProductID);
                var hid = node ? SelectNode(group) : SelectCore(group);
                if (hid is null)
                {
                    continue;
                }

                var serial = hid.SafeSerialNumber();
                var suffix = serial.Length > 0 ? serial : hid.StableInstanceKey();
                var id = node
                    ? $"corsair-node:{hid.ProductID:x4}:{suffix}"
                    : $"corsair-core:{hid.ProductID:x4}:{suffix}";
                if (existingDevices.FirstOrDefault(existing => existing.Id == id) is { } kept
                    && kept.Properties.TryGetValue("UsbPath", out var usbPath)
                    && string.Equals(usbPath, hid.DevicePath, StringComparison.Ordinal))
                {
                    devices.Add(kept);
                    continue;
                }

                try
                {
                    var manifest = manifests?.FindUsb(hid.VendorID, hid.ProductID);
                    if (node)
                    {
                        var manifestChannels = manifest?.LedChannels ?? 0;
                        var channels = manifestChannels > 0
                            ? manifestChannels
                            : CorsairProtocol.LightingNodeChannels(hid.ProductID);
                        devices.Add(CorsairLightingNodeDevice.Open(hid, id, channels, CorsairProtocol.DefaultChannelLeds, dryRun));
                    }
                    else
                    {
                        devices.Add(CorsairCommanderCoreDevice.Open(hid, id, CorsairProtocol.DefaultCommanderCoreLeds, dryRun));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogInitFailed(ex, hid.ProductID);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<IHardwareDevice>>(devices);
    }

    private static HidDevice? SelectNode(IEnumerable<HidDevice> interfaces) =>
        interfaces
            .Where(hid => hid.GetMaxOutputReportLength() >= CorsairProtocol.LightingNodeReportLength)
            .Where(hid => hid.HasUsagePage(CorsairProtocol.LightingNodeUsagePage) || hid.SafeTopLevelUsages().Count == 0)
            .OrderByDescending(hid => hid.HasUsagePage(CorsairProtocol.LightingNodeUsagePage))
            .FirstOrDefault();

    private static HidDevice? SelectCore(IEnumerable<HidDevice> interfaces) =>
        interfaces
            .Where(hid => hid.GetMaxOutputReportLength() >= CorsairProtocol.CommanderCoreMinReportLength)
            .OrderByDescending(hid => hid.HasUsagePage(CorsairProtocol.CommanderCoreUsagePage))
            .ThenByDescending(hid => hid.GetMaxOutputReportLength())
            .FirstOrDefault();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Initializing Corsair controller 0x{ProductId:X4} failed; is iCUE running?")]
    private partial void LogInitFailed(Exception exception, int productId);
}
