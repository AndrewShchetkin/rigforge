using HidSharp;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Hid;

namespace RigForge.Infrastructure.CrossPlatform.CoolerMaster;

/// <summary>Cooler Master ARGB Gen2 controller: three resizable ports in direct software mode.</summary>
internal sealed class CoolerMasterArgbDevice : IRgbDevice
{
    private readonly HidReportChannel _hid;
    private readonly bool _dryRun;
    private readonly Lock _transaction = new();
    private readonly Dictionary<string, string> _properties;
    private volatile IReadOnlyList<RgbZone> _zones;
    private bool _disposed;

    internal CoolerMasterArgbDevice(HidReportChannel hid, string id, string name, string usbPath, int productId, int ledsPerPort, bool dryRun)
    {
        _hid = hid;
        _dryRun = dryRun;
        Id = id;
        Name = name;
        var leds = Math.Clamp(ledsPerPort, 1, CoolerMasterArgbProtocol.MaxLedsPerPort);
        var zones = new RgbZone[CoolerMasterArgbProtocol.PortCount];
        for (var port = 0; port < zones.Length; port++)
        {
            zones[port] = new RgbZone
            {
                Id = $"port{port + 1}",
                Name = $"ARGB port {port + 1}",
                Topology = LedTopology.Linear,
                Leds = LedLayouts.Linear(leds),
                IsResizable = true,
                MaxLedCount = CoolerMasterArgbProtocol.MaxLedsPerPort,
            };
        }

        _zones = zones;
        _properties = new Dictionary<string, string>
        {
            ["ProductId"] = $"0x{productId:X4}",
            ["vidPid"] = UsbVidPid.Format(hid.Device, CoolerMasterArgbProtocol.VendorId, productId),
            ["UsbPath"] = usbPath,
        };
    }

    public string Id { get; }

    public string Name { get; }

    public string Vendor => "Cooler Master";

    public DeviceCapabilities Capabilities => DeviceCapabilities.Rgb;

    public IReadOnlyDictionary<string, string> Properties => _properties;

    public IReadOnlyList<RgbZone> Zones => _zones;

    public int MaxFramesPerSecond => 30;

    public static CoolerMasterArgbDevice Open(HidDevice hidDevice, string id, bool dryRun)
    {
        var hid = HidReportChannel.Open(hidDevice);
        try
        {
            if (!dryRun)
            {
                Span<byte> packet = stackalloc byte[CoolerMasterArgbProtocol.ReportLength];
                for (byte port = 0; port < CoolerMasterArgbProtocol.PortCount; port++)
                {
                    CoolerMasterArgbProtocol.WriteDirectMode(port, packet);
                    hid.Write(packet);
                }
            }

            var product = hidDevice.SafeProductName();
            return new CoolerMasterArgbDevice(
                hid,
                id,
                product.Length > 0 ? product : "Cooler Master ARGB",
                hidDevice.DevicePath,
                hidDevice.ProductID,
                CoolerMasterArgbProtocol.DefaultLedsPerPort,
                dryRun);
        }
        catch
        {
            hid.Dispose();
            throw;
        }
    }

    public bool TryResizeZone(string zoneId, int ledCount)
    {
        if (ledCount < 1)
        {
            return false;
        }

        var copy = _zones.ToArray();
        var index = Array.FindIndex(copy, zone => zone.Id == zoneId && zone.IsResizable);
        if (index < 0 || ledCount > copy[index].MaxLedCount)
        {
            return false;
        }

        copy[index] = copy[index] with { Leds = LedLayouts.Linear(ledCount) };
        _zones = copy;
        return true;
    }

    public ValueTask UpdateLedsAsync(ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken)
    {
        if (_dryRun || colors.IsEmpty)
        {
            return ValueTask.CompletedTask;
        }

        lock (_transaction)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Span<byte> packet = stackalloc byte[CoolerMasterArgbProtocol.ReportLength];
            var zones = _zones;
            var offset = 0;
            for (var port = 0; port < zones.Count && offset < colors.Length; port++)
            {
                var count = Math.Min(zones[port].LedCount, colors.Length - offset);
                if (count > 0)
                {
                    var slice = colors.Span.Slice(offset, count);
                    CoolerMasterArgbProtocol.WriteDirectMode((byte)port, packet);
                    _hid.Write(packet);
                    var packetCount = CoolerMasterArgbProtocol.PacketCount(count);
                    for (var packetIndex = 0; packetIndex < packetCount; packetIndex++)
                    {
                        CoolerMasterArgbProtocol.WriteColorPacket((byte)port, packetIndex, slice, packet);
                        _hid.Write(packet);
                    }
                }

                offset += zones[port].LedCount;
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        lock (_transaction)
        {
            if (!_disposed)
            {
                _disposed = true;
                _hid.Dispose();
            }
        }

        return ValueTask.CompletedTask;
    }
}
