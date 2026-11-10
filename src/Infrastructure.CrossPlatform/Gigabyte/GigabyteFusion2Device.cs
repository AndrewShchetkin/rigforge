using HidSharp;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Hid;

namespace RigForge.Infrastructure.CrossPlatform.Gigabyte;

/// <summary>RGB Fusion 2.0 USB controller. Frames end with execute 0x28 and never a flash save.</summary>
internal sealed class GigabyteFusion2Device : IRgbDevice
{
    private readonly HidReportChannel _hid;
    private readonly bool _dryRun;
    private readonly Lock _transaction = new();
    private readonly Dictionary<string, string> _properties;
    private volatile IReadOnlyList<RgbZone> _zones;
    private bool _disposed;

    internal GigabyteFusion2Device(HidReportChannel hid, string id, string name, string usbPath, int productId, int argbLeds, bool dryRun)
    {
        _hid = hid;
        _dryRun = dryRun;
        Id = id;
        Name = name;
        var leds = Math.Clamp(argbLeds, 1, GigabyteFusion2Protocol.MaxAddressableLeds);
        _zones =
        [
            Fixed("onboard", "Onboard"),
            Argb("dled1", "D_LED1", leds),
            Argb("dled2", "D_LED2", leds),
        ];
        _properties = new Dictionary<string, string>
        {
            ["ProductId"] = $"0x{productId:X4}",
            ["vidPid"] = UsbVidPid.Format(hid.Device, GigabyteFusion2Protocol.VendorId, productId),
            ["UsbPath"] = usbPath,
        };
    }

    public string Id { get; }

    public string Name { get; }

    public string Vendor => "Gigabyte";

    public DeviceCapabilities Capabilities => DeviceCapabilities.Rgb;

    public IReadOnlyDictionary<string, string> Properties => _properties;

    public IReadOnlyList<RgbZone> Zones => _zones;

    public int MaxFramesPerSecond => 30;

    public static GigabyteFusion2Device Open(HidDevice hidDevice, string id, int argbLeds, bool dryRun)
    {
        var hid = HidReportChannelFactory.OpenOutputOrFeature(hidDevice, GigabyteFusion2Protocol.ReportLength);
        try
        {
            var product = hidDevice.SafeProductName();
            return new GigabyteFusion2Device(
                hid,
                id,
                product.Length > 0 ? product : "Gigabyte RGB Fusion 2.0",
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
            Span<byte> packet = stackalloc byte[GigabyteFusion2Protocol.ReportLength];
            var zones = _zones;
            var offset = 0;
            if (zones.Count > 0 && offset < colors.Length)
            {
                GigabyteFusion2Protocol.WriteOnboardPacket(GigabyteFusion2Protocol.OnboardHeader, colors.Span[offset], packet);
                _hid.Write(packet);
                offset += zones[0].LedCount;
            }

            WriteHeader(GigabyteFusion2Protocol.DLed1, zones, 1, colors.Span, ref offset, packet);
            WriteHeader(GigabyteFusion2Protocol.DLed2, zones, 2, colors.Span, ref offset, packet);
            GigabyteFusion2Protocol.WriteExecute(packet);
            _hid.Write(packet);
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

    private void WriteHeader(byte header, IReadOnlyList<RgbZone> zones, int zoneIndex, ReadOnlySpan<RgbColor> colors, ref int offset, Span<byte> packet)
    {
        if (zoneIndex >= zones.Count || offset >= colors.Length)
        {
            return;
        }

        var count = Math.Min(zones[zoneIndex].LedCount, colors.Length - offset);
        if (count > 0)
        {
            var slice = colors.Slice(offset, count);
            var packetCount = GigabyteFusion2Protocol.PacketCount(count);
            for (var packetIndex = 0; packetIndex < packetCount; packetIndex++)
            {
                GigabyteFusion2Protocol.WriteAddressablePacket(header, slice, packetIndex, packet);
                _hid.Write(packet);
            }
        }

        offset += zones[zoneIndex].LedCount;
    }

    private static RgbZone Fixed(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Topology = LedTopology.Linear,
        Leds = LedLayouts.Linear(1),
        MaxLedCount = 1,
    };

    private static RgbZone Argb(string id, string name, int leds) => new()
    {
        Id = id,
        Name = name,
        Topology = LedTopology.Linear,
        Leds = LedLayouts.Linear(leds),
        IsResizable = true,
        MaxLedCount = GigabyteFusion2Protocol.MaxAddressableLeds,
    };
}
