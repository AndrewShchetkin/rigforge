using HidSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;
using RigForge.Infrastructure.CrossPlatform.Hid;

namespace RigForge.Infrastructure.CrossPlatform.Peripherals.QmkVial;

/// <summary>
/// Finds QMK / VIA / Vial keyboards on raw HID <c>0xFF60:0x0061</c> (any VID/PID).
/// Boards that answer VIA but do not expose VialRGB are closed and skipped.
/// </summary>
internal sealed partial class QmkVialProbe(IOptions<HardwareOptions> options, ILogger<QmkVialProbe> logger) : IDeviceProbe
{
    public string Name => "QMK / VialRGB";

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken) =>
        ProbeAsync([], cancellationToken);

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(
        IReadOnlyList<IHardwareDevice> existingDevices,
        CancellationToken cancellationToken)
    {
        var dryRun = options.Value.DryRun;
        var devices = new List<IHardwareDevice>();
        IEnumerable<HidDevice> interfaces;
        try
        {
            interfaces = DeviceList.Local.GetHidDevices().Where(QmkVialDevice.IsRawHidInterface);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogEnumerationFailed(ex);
            return Task.FromResult<IReadOnlyList<IHardwareDevice>>(devices);
        }

        foreach (var group in interfaces.GroupBy(hid => (hid.VendorID, hid.ProductID, Key: hid.StableInstanceKey())))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hid = group.OrderByDescending(static item => SafeLength(item)).First();
            var serial = hid.SafeSerialNumber();
            var suffix = serial.Length > 0 ? serial : hid.StableInstanceKey();
            var id = $"qmk-vial:{hid.VendorID:x4}:{hid.ProductID:x4}:{suffix}";
            if (existingDevices.FirstOrDefault(existing => existing.Id == id) is { } kept
                && kept.Properties.TryGetValue("UsbPath", out var usbPath)
                && string.Equals(usbPath, hid.DevicePath, StringComparison.Ordinal))
            {
                devices.Add(kept);
                continue;
            }

            var displayName = hid.SafeProductName();
            if (displayName.Length == 0)
            {
                displayName = $"QMK {hid.VendorID:X4}:{hid.ProductID:X4}";
            }

            try
            {
                var device = QmkVialDevice.TryOpen(hid, id, dryRun, out var vialRgbDisabled);
                if (device is not null)
                {
                    devices.Add(device);
                    continue;
                }

                if (vialRgbDisabled)
                {
                    LogVialRgbDisabled(displayName);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogInitFailed(ex, hid.VendorID, hid.ProductID);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "QMK/Vial keyboard {Name} detected on 0xFF60, VialRGB direct protocol not enabled in firmware")]
    private partial void LogVialRgbDisabled(string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Initializing QMK/Vial keyboard {VendorId:X4}:{ProductId:X4} failed")]
    private partial void LogInitFailed(Exception exception, int vendorId, int productId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Enumerating QMK raw HID interfaces failed")]
    private partial void LogEnumerationFailed(Exception exception);
}
