using HidSharp;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Hid;

namespace RigForge.Infrastructure.CrossPlatform.AsRock;

/// <summary>ASRock Polychrome USB in volatile direct mode. The empty 0x12 flash commit is never sent.</summary>
internal sealed class AsRockPolychromeDevice : IRgbDevice
{
    private readonly HidReportChannel _hid;
    private readonly bool _dryRun;
    private readonly Lock _transaction = new();
    private readonly Dictionary<string, string> _properties;
    private volatile IReadOnlyList<RgbZone> _zones;
    private bool _disposed;

    internal AsRockPolychromeDevice(HidReportChannel hid, string id, string name, string usbPath, int productId, int argbLeds, bool dryRun)
    {
        _hid = hid;
        _dryRun = dryRun;
        Id = id;
        Name = name;
        var leds = Math.Clamp(argbLeds, 1, AsRockPolychromeProtocol.MaxArgbLeds);
        _zones =
        [
            new RgbZone
            {
                Id = "onboard",
                Name = "Onboard",
                Topology = LedTopology.Linear,
                Leds = LedLayouts.Linear(1),
                MaxLedCount = 1,
            },
            new RgbZone
            {
                Id = "argb",
                Name = "ARGB",
                Topology = LedTopology.Linear,
                Leds = LedLayouts.Linear(leds),
                IsResizable = true,
                MaxLedCount = AsRockPolychromeProtocol.MaxArgbLeds,
            },
        ];
        _properties = new Dictionary<string, string>
        {
            ["ProductId"] = $"0x{productId:X4}",
            ["vidPid"] = UsbVidPid.Format(hid.Device, AsRockPolychromeProtocol.VendorId, productId),
            ["UsbPath"] = usbPath,
        };
    }

    public string Id { get; }

    public string Name { get; }

    public string Vendor => "ASRock";

    public DeviceCapabilities Capabilities => DeviceCapabilities.Rgb;

    public IReadOnlyDictionary<string, string> Properties => _properties;

    public IReadOnlyList<RgbZone> Zones => _zones;

    public int MaxFramesPerSecond => 20;

    public static AsRockPolychromeDevice Open(HidDevice hidDevice, string id, int argbLeds, bool dryRun)
    {
        var hid = HidReportChannel.Open(hidDevice);
        try
        {
            var product = hidDevice.SafeProductName();
            return new AsRockPolychromeDevice(
                hid,
                id,
                product.Length > 0 ? product : "ASRock Polychrome USB",
                hidDevice.DevicePath,
                hidDevice.ProductID,
                argbLeds,
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
            Span<byte> packet = stackalloc byte[AsRockPolychromeProtocol.ReportLength];
            var zones = _zones;
            var offset = 0;
            if (zones.Count > 0 && offset < colors.Length)
            {
                AsRockPolychromeProtocol.WriteOnboardPacket(AsRockPolychromeProtocol.OnboardChannel, colors.Span[offset], packet);
                _hid.Write(packet);
                offset += zones[0].LedCount;
            }

            if (zones.Count > 1 && offset < colors.Length)
            {
                var count = Math.Min(zones[1].LedCount, colors.Length - offset);
                if (count > 0)
                {
                    var slice = colors.Span.Slice(offset, count);
                    var packetCount = AsRockPolychromeProtocol.PacketCount(count);
                    for (var packetIndex = 0; packetIndex < packetCount; packetIndex++)
                    {
                        AsRockPolychromeProtocol.WriteAddressablePacket(AsRockPolychromeProtocol.ArgbChannel, packetIndex, slice, packet);
                        _hid.Write(packet);
                    }
                }
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
