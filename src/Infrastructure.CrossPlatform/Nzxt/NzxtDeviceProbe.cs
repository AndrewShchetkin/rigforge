using HidSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;
using RigForge.Infrastructure.CrossPlatform.Hid;
using RigForge.Infrastructure.CrossPlatform.Usb;

namespace RigForge.Infrastructure.CrossPlatform.Nzxt;

internal sealed partial class NzxtDeviceProbe(
    IUsbBulkTransportFactory bulkFactory,
    IOptions<HardwareOptions> options,
    ILogger<NzxtDeviceProbe> logger) : IDeviceProbe
{
    public string Name => "NZXT USB HID";

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Nzxt.Enabled)
        {
            return Task.FromResult<IReadOnlyList<IHardwareDevice>>([]);
        }

        var devices = new List<IHardwareDevice>();

        // Windows lists one entry per HID collection; keep the one with the widest output report per physical device.
        var candidates = DeviceList.Local.GetHidDevices(NzxtCatalog.VendorId)
            .Where(d => d.GetMaxOutputReportLength() > 0)
            .GroupBy(d => (d.ProductID, Serial: d.SafeSerialNumber(), Path: d.SafeSerialNumber().Length == 0 ? d.DevicePath : string.Empty))
            .Select(g => g.OrderByDescending(d => d.GetMaxOutputReportLength()).First())
            .ToList();

        foreach (var hid in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var serial = hid.SafeSerialNumber();
            var suffix = serial.Length > 0 ? serial : $"#{devices.Count(d => d.Id.Contains($"{hid.ProductID:x4}", StringComparison.Ordinal))}";

            try
            {
                if (NzxtCatalog.Krakens.TryGetValue(hid.ProductID, out var kraken))
                {
                    devices.Add(NzxtKrakenDevice.Open(hid, kraken, $"nzxt:{hid.ProductID:x4}:{suffix}", bulkFactory, settings.Nzxt, settings.DryRun, logger));
                }
                else if (NzxtCatalog.Hue2Devices.TryGetValue(hid.ProductID, out var hue2))
                {
                    devices.Add(NzxtHue2Device.Open(hid, hue2, $"nzxt:{hid.ProductID:x4}:{suffix}", settings.Nzxt, settings.DryRun));
                }
                else
                {
                    LogUnsupported(hid.ProductID, hid.SafeProductName());
                }
            }
            catch (Exception ex)
            {
                LogInitFailed(ex, hid.ProductID, hid.SafeProductName());
            }
        }

        return Task.FromResult<IReadOnlyList<IHardwareDevice>>(devices);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "NZXT device 0x{ProductId:X4} ({Product}) is not supported yet")]
    private partial void LogUnsupported(int productId, string product);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Initializing NZXT device 0x{ProductId:X4} ({Product}) failed; is NZXT CAM running?")]
    private partial void LogInitFailed(Exception exception, int productId, string product);
}
