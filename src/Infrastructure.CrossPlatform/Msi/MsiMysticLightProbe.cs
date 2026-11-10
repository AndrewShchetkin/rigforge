using HidSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Hid;

namespace RigForge.Infrastructure.CrossPlatform.Msi;

internal sealed partial class MsiMysticLightProbe(
    IOptions<HardwareOptions> options,
    ILogger<MsiMysticLightProbe> logger,
    IDeviceManifestCatalog? manifests = null) : IDeviceProbe
{
    public string Name => "MSI Mystic Light USB";

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken) =>
        ProbeAsync([], cancellationToken);

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(
        IReadOnlyList<IHardwareDevice> existingDevices,
        CancellationToken cancellationToken)
    {
        var dryRun = options.Value.DryRun;
        var vendorIds = new HashSet<int>(MsiMysticLightProtocol.VendorIds);
        var productIds = new HashSet<int>(MsiMysticLightProtocol.ProductIds);
        if (manifests is not null)
        {
            foreach (var manifest in manifests.ForProtocol(DeviceProtocolFamily.MsiMysticLightUsb))
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
                var hid = group.OrderByDescending(ReportKind).FirstOrDefault(candidate => ReportKind(candidate) > 0);
                if (hid is null)
                {
                    continue;
                }

                var serial = hid.SafeSerialNumber();
                var suffix = serial.Length > 0 ? serial : hid.StableInstanceKey();
                var id = $"msi-mystic:{hid.ProductID:x4}:{suffix}";
                if (existingDevices.FirstOrDefault(existing => existing.Id == id) is { } kept
                    && kept.Properties.TryGetValue("UsbPath", out var usbPath)
                    && string.Equals(usbPath, hid.DevicePath, StringComparison.Ordinal))
                {
                    devices.Add(kept);
                    continue;
                }

                var kind = ReportKind(hid) >= MsiMysticLightProtocol.ReportLength
                    ? MsiReportKind.Zones185
                    : MsiReportKind.ArgbStream;
                try
                {
                    devices.Add(MsiMysticLightDevice.Open(hid, id, kind, dryRun));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogInitFailed(ex, hid.ProductID);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<IHardwareDevice>>(devices);
    }

    internal static int ReportKind(HidDevice device)
    {
        var output = SafeLength(device.GetMaxOutputReportLength);
        var feature = SafeLength(device.GetMaxFeatureReportLength);
        if (output == MsiMysticLightProtocol.ReportLength || feature == MsiMysticLightProtocol.ReportLength)
        {
            return MsiMysticLightProtocol.ReportLength;
        }

        if (output is 64 or 65)
        {
            return output;
        }

        if (feature is 64 or 65)
        {
            return feature;
        }

        return 0;
    }

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Initializing MSI Mystic Light controller 0x{ProductId:X4} failed; is MSI Center running?")]
    private partial void LogInitFailed(Exception exception, int productId);
}
