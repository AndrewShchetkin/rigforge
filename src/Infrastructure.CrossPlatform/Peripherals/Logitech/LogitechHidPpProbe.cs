using HidSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Hid;

namespace RigForge.Infrastructure.CrossPlatform.Peripherals.Logitech;

internal sealed partial class LogitechHidPpProbe(
    IOptions<HardwareOptions> options,
    ILogger<LogitechHidPpProbe> logger,
    IDeviceManifestCatalog? manifests = null) : IDeviceProbe
{
    public string Name => "Logitech HID++ 2.0";

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken) =>
        ProbeAsync([], cancellationToken);

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(
        IReadOnlyList<IHardwareDevice> existingDevices,
        CancellationToken cancellationToken)
    {
        var dryRun = options.Value.DryRun;
        var productIds = new HashSet<int>(LogitechHidPpProtocol.ProductIds);
        if (manifests is not null)
        {
            foreach (var manifest in manifests.ForProtocol(DeviceProtocolFamily.LogitechHidPp))
            {
                productIds.Add(manifest.ProductId);
            }
        }

        var devices = new List<IHardwareDevice>();
        foreach (var hid in Enumerate(productIds))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var serial = hid.SafeSerialNumber();
            var suffix = serial.Length > 0 ? serial : hid.StableInstanceKey();
            var id = $"logitech-hidpp:{hid.ProductID:x4}:{suffix}";
            if (existingDevices.FirstOrDefault(existing => existing.Id == id) is LogitechHidPpDevice kept
                && kept.IsOnline
                && kept.Properties.TryGetValue("UsbPath", out var usbPath)
                && string.Equals(usbPath, hid.DevicePath, StringComparison.Ordinal))
            {
                devices.Add(kept);
                continue;
            }

            try
            {
                devices.Add(LogitechHidPpDevice.Open(hid, id, dryRun));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogInitFailed(ex, hid.ProductID);
            }
        }

        return Task.FromResult<IReadOnlyList<IHardwareDevice>>(devices);
    }

    private static IEnumerable<HidDevice> Enumerate(HashSet<int> productIds)
    {
        List<HidDevice> found;
        try
        {
            found = DeviceList.Local.GetHidDevices(LogitechHidPpProtocol.VendorId)
                .Where(hid => productIds.Contains(hid.ProductID) && LogitechHidPpDevice.IsLongReportInterface(hid))
                .ToList();
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (var group in found.GroupBy(hid => (hid.ProductID, Key: hid.StableInstanceKey())))
        {
            yield return group.OrderByDescending(static hid => SafeLength(hid)).First();
        }
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Initializing Logitech HID++ device 0x{ProductId:X4} failed")]
    private partial void LogInitFailed(Exception exception, int productId);
}
