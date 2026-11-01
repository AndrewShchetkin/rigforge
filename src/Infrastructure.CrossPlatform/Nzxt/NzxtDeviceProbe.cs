using HidSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Hid;
using RigForge.Infrastructure.CrossPlatform.Usb;

namespace RigForge.Infrastructure.CrossPlatform.Nzxt;

internal sealed partial class NzxtDeviceProbe(
    IUsbBulkTransportFactory bulkFactory,
    IOptions<HardwareOptions> options,
    ILogger<NzxtDeviceProbe> logger,
    IDeviceManifestCatalog? manifests = null) : IDeviceProbe
{
    public string Name => "NZXT USB HID";

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken) =>
        ProbeAsync([], cancellationToken);

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(
        IReadOnlyList<IHardwareDevice> existingDevices,
        CancellationToken cancellationToken)
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
            var suffix = serial.Length > 0 ? serial : hid.StableInstanceKey();
            var id = $"nzxt:{hid.ProductID:x4}:{suffix}";

            if (existingDevices.FirstOrDefault(e => e.Id == id) is { } existing)
            {
                devices.Add(existing);
                continue;
            }

            try
            {
                var manifest = manifests?.FindUsb(NzxtCatalog.VendorId, hid.ProductID);
                if (NzxtCatalog.Krakens.TryGetValue(hid.ProductID, out var kraken) || manifest?.Protocol == DeviceProtocolFamily.NzxtKraken)
                {
                    var model = manifest?.Protocol == DeviceProtocolFamily.NzxtKraken
                        ? new KrakenModel(
                            hid.ProductID,
                            manifest.Name,
                            manifest.Display?.Width ?? kraken?.LcdWidth ?? 240,
                            manifest.Display?.Height ?? kraken?.LcdHeight ?? 240,
                            manifest.PumpHeader ?? kraken?.PumpHeader ?? [0x01, 0x01, 0x00],
                            manifest.FanHeader ?? kraken?.FanHeader ?? [0x02, 0x01, 0x01],
                            manifest.Display?.Experimental ?? kraken?.LcdExperimental ?? false)
                        : kraken!;
                    devices.Add(NzxtKrakenDevice.Open(hid, model, id, bulkFactory, settings.Nzxt, settings.DryRun, logger));
                }
                else if (NzxtCatalog.Hue2Devices.TryGetValue(hid.ProductID, out var hue2) || manifest?.Protocol == DeviceProtocolFamily.NzxtHue2)
                {
                    var model = manifest?.Protocol == DeviceProtocolFamily.NzxtHue2
                        ? new Hue2Model(hid.ProductID, manifest.Name, manifest.FanChannels, manifest.LedChannels)
                        : hue2!;
                    devices.Add(NzxtHue2Device.Open(hid, model, id, settings.Nzxt, settings.DryRun));
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
