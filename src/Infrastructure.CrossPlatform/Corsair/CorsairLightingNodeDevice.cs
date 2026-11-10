using HidSharp;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Hid;

namespace RigForge.Infrastructure.CrossPlatform.Corsair;

/// <summary>Lighting Node Pro/Core, Commander Pro and LS100 in software direct mode.</summary>
internal sealed class CorsairLightingNodeDevice : IRgbDevice
{
    private readonly HidReportChannel _hid;
    private readonly bool _dryRun;
    private readonly Lock _transaction = new();
    private readonly Dictionary<string, string> _properties;
    private volatile IReadOnlyList<RgbZone> _zones;
    private bool _disposed;

    internal CorsairLightingNodeDevice(
        HidReportChannel hid,
        string id,
        string name,
        string usbPath,
        int productId,
        int channelCount,
        int ledsPerChannel,
        bool dryRun)
    {
        _hid = hid;
        _dryRun = dryRun;
        Id = id;
        Name = name;
        var count = Math.Clamp(channelCount, 1, 2);
        var leds = Math.Clamp(ledsPerChannel, 1, CorsairProtocol.MaxChannelLeds);
        var zones = new RgbZone[count];
        for (var channel = 0; channel < count; channel++)
        {
            zones[channel] = LinearZone($"channel{channel + 1}", $"Channel {channel + 1}", leds, CorsairProtocol.MaxChannelLeds);
        }

        _zones = zones;
        _properties = new Dictionary<string, string>
        {
            ["ProductId"] = $"0x{productId:X4}",
            ["vidPid"] = UsbVidPid.Format(hid.Device, CorsairProtocol.VendorId, productId),
            ["UsbPath"] = usbPath,
            ["Channels"] = count.ToString(),
        };
    }

    public string Id { get; }

    public string Name { get; }

    public string Vendor => "Corsair";

    public DeviceCapabilities Capabilities => DeviceCapabilities.Rgb;

    public IReadOnlyDictionary<string, string> Properties => _properties;

    public IReadOnlyList<RgbZone> Zones => _zones;

    public int MaxFramesPerSecond => 20;

    public static CorsairLightingNodeDevice Open(HidDevice hidDevice, string id, int channelCount, int ledsPerChannel, bool dryRun)
    {
        var hid = HidReportChannel.Open(hidDevice);
        try
        {
            var product = hidDevice.SafeProductName();
            var device = new CorsairLightingNodeDevice(
                hid,
                id,
                product.Length > 0 ? product : "Corsair Lighting Node",
                hidDevice.DevicePath,
                hidDevice.ProductID,
                channelCount,
                ledsPerChannel,
                dryRun);

            if (!dryRun)
            {
                Span<byte> packet = stackalloc byte[CorsairProtocol.LightingNodeReportLength];
                for (byte channel = 0; channel < device._zones.Count; channel++)
                {
                    CorsairProtocol.WriteLightingNodeSoftwareMode(channel, packet);
                    hid.Write(packet);
                    CorsairProtocol.WriteLightingNodeBegin(channel, packet);
                    hid.Write(packet);
                }

                CorsairProtocol.WriteLightingNodeCommit(packet);
                hid.Write(packet);
            }

            return device;
        }
        catch
        {
            hid.Dispose();
            throw;
        }
    }

    public bool TryResizeZone(string zoneId, int ledCount)
    {
        var zones = _zones;
        if (ledCount < 1)
        {
            return false;
        }

        var copy = zones.ToArray();
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
        if (_dryRun)
        {
            return ValueTask.CompletedTask;
        }

        var zones = _zones;
        lock (_transaction)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Span<byte> packet = stackalloc byte[CorsairProtocol.LightingNodeReportLength];
            var offset = 0;
            var wrote = false;
            for (var channel = 0; channel < zones.Count && offset < colors.Length; channel++)
            {
                var count = Math.Min(zones[channel].LedCount, colors.Length - offset);
                if (count > 0)
                {
                    var channelColors = colors.Span.Slice(offset, count);
                    CorsairProtocol.WriteLightingNodeBegin((byte)channel, packet);
                    _hid.Write(packet);
                    var packetCount = CorsairProtocol.LightingNodeColorPacketCount(count);
                    for (var packetIndex = 0; packetIndex < packetCount; packetIndex++)
                    {
                        CorsairProtocol.WriteLightingNodeColorPacket((byte)channel, channelColors, packetIndex, packet);
                        _hid.Write(packet);
                    }

                    wrote = true;
                }

                offset += zones[channel].LedCount;
            }

            if (wrote)
            {
                CorsairProtocol.WriteLightingNodeCommit(packet);
                _hid.Write(packet);
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        lock (_transaction)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            if (!_dryRun)
            {
                try
                {
                    Span<byte> packet = stackalloc byte[CorsairProtocol.LightingNodeReportLength];
                    for (byte channel = 0; channel < _zones.Count; channel++)
                    {
                        CorsairProtocol.WriteLightingNodeHardwareMode(channel, packet);
                        _hid.Write(packet);
                    }

                    CorsairProtocol.WriteLightingNodeCommit(packet);
                    _hid.Write(packet);
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or ObjectDisposedException or InvalidOperationException)
                {
                }
            }

            _hid.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private static RgbZone LinearZone(string id, string name, int leds, int max) => new()
    {
        Id = id,
        Name = name,
        Topology = LedTopology.Linear,
        Leds = LedLayouts.Linear(leds),
        IsResizable = true,
        MaxLedCount = max,
    };
}
