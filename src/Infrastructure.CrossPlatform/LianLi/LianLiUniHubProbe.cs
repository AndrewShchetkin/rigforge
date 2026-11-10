using HidSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Hid;

namespace RigForge.Infrastructure.CrossPlatform.LianLi;

internal sealed partial class LianLiUniHubProbe(
    IOptions<HardwareOptions> options,
    ILogger<LianLiUniHubProbe> logger,
    IDeviceManifestCatalog? manifests = null) : IDeviceProbe
{
    public string Name => "Lian Li UNI HUB";

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken) =>
        ProbeAsync([], cancellationToken);

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(
        IReadOnlyList<IHardwareDevice> existingDevices,
        CancellationToken cancellationToken)
    {
        var dryRun = options.Value.DryRun;
        var vendorIds = new HashSet<int> { LianLiUniHubProtocol.VendorId };
        var productIds = new HashSet<int>(LianLiUniHubProtocol.ProductIds);
        if (manifests is not null)
        {
            foreach (var manifest in manifests.ForProtocol(DeviceProtocolFamily.LianLiUniHub))
            {
                vendorIds.Add(manifest.VendorId);
                productIds.Add(manifest.ProductId);
            }
        }

        var devices = new List<IHardwareDevice>();
        foreach (var vendorId in vendorIds)
        {
            var grouped = DeviceList.Local.GetHidDevices(vendorId)
                .Where(hid => productIds.Contains(hid.ProductID) && hid.GetMaxOutputReportLength() >= LianLiUniHubProtocol.ReportLength)
                .GroupBy(hid => (hid.ProductID, Key: hid.StableInstanceKey()))
                .Select(group => group.OrderByDescending(hid => hid.GetMaxOutputReportLength()).First());

            foreach (var hid in grouped)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var serial = hid.SafeSerialNumber();
                var suffix = serial.Length > 0 ? serial : hid.StableInstanceKey();
                var id = $"lianli-unihub:{hid.ProductID:x4}:{suffix}";
                if (existingDevices.FirstOrDefault(existing => existing.Id == id) is { } kept
                    && kept.Properties.TryGetValue("UsbPath", out var usbPath)
                    && string.Equals(usbPath, hid.DevicePath, StringComparison.Ordinal))
                {
                    devices.Add(kept);
                    continue;
                }

                try
                {
                    devices.Add(LianLiUniHubDevice.Open(hid, id, dryRun));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogInitFailed(ex, hid.ProductID);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<IHardwareDevice>>(devices);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Initializing Lian Li UNI HUB 0x{ProductId:X4} failed; is L-Connect running?")]
    private partial void LogInitFailed(Exception exception, int productId);
}
