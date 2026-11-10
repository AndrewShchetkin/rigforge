using HidSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Hid;

namespace RigForge.Infrastructure.CrossPlatform.Gigabyte;

internal sealed partial class GigabyteFusion2Probe(
    IOptions<HardwareOptions> options,
    ILogger<GigabyteFusion2Probe> logger,
    IDeviceManifestCatalog? manifests = null) : IDeviceProbe
{
    public string Name => "Gigabyte RGB Fusion 2.0";

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken) =>
        ProbeAsync([], cancellationToken);

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(
        IReadOnlyList<IHardwareDevice> existingDevices,
        CancellationToken cancellationToken)
    {
        var dryRun = options.Value.DryRun;
        var vendorIds = new HashSet<int> { GigabyteFusion2Protocol.VendorId };
        var productIds = new HashSet<int>(GigabyteFusion2Protocol.ProductIds);
        if (manifests is not null)
        {
            foreach (var manifest in manifests.ForProtocol(DeviceProtocolFamily.GigabyteFusion2Usb))
            {
                vendorIds.Add(manifest.VendorId);
                productIds.Add(manifest.ProductId);
            }
        }

        var devices = new List<IHardwareDevice>();
        foreach (var vendorId in vendorIds)
        {
            var grouped = DeviceList.Local.GetHidDevices(vendorId)
                .Where(hid => productIds.Contains(hid.ProductID))
                .GroupBy(hid => (hid.ProductID, Key: hid.StableInstanceKey()));

            foreach (var group in grouped)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hid = group
                    .Where(candidate => ReportLength(candidate) >= GigabyteFusion2Protocol.ReportLength)
                    .OrderBy(candidate => ReportLength(candidate))
                    .FirstOrDefault();
                if (hid is null)
                {
                    continue;
                }

                var serial = hid.SafeSerialNumber();
                var suffix = serial.Length > 0 ? serial : hid.StableInstanceKey();
                var id = $"gigabyte-fusion2:{hid.ProductID:x4}:{suffix}";
                if (existingDevices.FirstOrDefault(existing => existing.Id == id) is { } kept
                    && kept.Properties.TryGetValue("UsbPath", out var usbPath)
                    && string.Equals(usbPath, hid.DevicePath, StringComparison.Ordinal))
                {
                    devices.Add(kept);
                    continue;
                }

                try
                {
                    devices.Add(GigabyteFusion2Device.Open(hid, id, GigabyteFusion2Protocol.DefaultAddressableLeds, dryRun));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogInitFailed(ex, hid.ProductID);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<IHardwareDevice>>(devices);
    }

    private static int ReportLength(HidDevice device)
    {
        var output = Safe(device.GetMaxOutputReportLength);
        var feature = Safe(device.GetMaxFeatureReportLength);
        return Math.Max(output, feature);
    }

    private static int Safe(Func<int> read)
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Initializing Gigabyte RGB Fusion controller 0x{ProductId:X4} failed; is RGB Fusion running?")]
    private partial void LogInitFailed(Exception exception, int productId);
}
