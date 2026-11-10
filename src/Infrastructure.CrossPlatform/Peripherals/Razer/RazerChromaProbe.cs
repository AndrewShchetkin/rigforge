using HidSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Hid;

namespace RigForge.Infrastructure.CrossPlatform.Peripherals.Razer;

internal sealed partial class RazerChromaProbe(
    IOptions<HardwareOptions> options,
    ILogger<RazerChromaProbe> logger,
    IDeviceManifestCatalog? manifests = null) : IDeviceProbe
{
    public string Name => "Razer Chroma HID";

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken) =>
        ProbeAsync([], cancellationToken);

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(
        IReadOnlyList<IHardwareDevice> existingDevices,
        CancellationToken cancellationToken)
    {
        var dryRun = options.Value.DryRun;
        var productIds = new HashSet<int>(RazerChromaProtocol.ProductIds);
        if (manifests is not null)
        {
            foreach (var manifest in manifests.ForProtocol(DeviceProtocolFamily.RazerChromaHid))
            {
                productIds.Add(manifest.ProductId);
            }
        }

        var devices = new List<IHardwareDevice>();
        List<HidDevice> found;
        try
        {
            found = DeviceList.Local.GetHidDevices(RazerChromaProtocol.VendorId)
                .Where(hid => productIds.Contains(hid.ProductID) && RazerChromaDevice.IsChromaInterface(hid))
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogEnumerationFailed(ex);
            return Task.FromResult<IReadOnlyList<IHardwareDevice>>(devices);
        }

        foreach (var group in found.GroupBy(hid => (hid.ProductID, Key: hid.StableInstanceKey())))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hid = group.OrderByDescending(static item => SafeLength(item)).First();
            var serial = hid.SafeSerialNumber();
            var suffix = serial.Length > 0 ? serial : hid.StableInstanceKey();
            var id = $"razer-chroma:{hid.ProductID:x4}:{suffix}";
            if (existingDevices.FirstOrDefault(existing => existing.Id == id) is { } kept
                && kept.Properties.TryGetValue("UsbPath", out var usbPath)
                && string.Equals(usbPath, hid.DevicePath, StringComparison.Ordinal))
            {
                devices.Add(kept);
                continue;
            }

            try
            {
                devices.Add(RazerChromaDevice.Open(hid, id, dryRun));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogInitFailed(ex, hid.ProductID);
            }
        }

        return Task.FromResult<IReadOnlyList<IHardwareDevice>>(devices);
    }

    private static int SafeLength(HidDevice device)
    {
        try
        {
            return device.GetMaxOutputReportLength();
        }
        catch (Exception)
        {
            return 0;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Initializing Razer Chroma device 0x{ProductId:X4} failed")]
    private partial void LogInitFailed(Exception exception, int productId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Enumerating Razer Chroma HID interfaces failed")]
    private partial void LogEnumerationFailed(Exception exception);
}
