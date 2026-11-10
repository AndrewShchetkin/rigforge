using HidSharp;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Hid;

namespace RigForge.Infrastructure.CrossPlatform.Asus;

/// <summary>ASUS Aura USB mainboard controller in direct mode: onboard zone plus the ARGB (ADD_GEN2) headers.</summary>
internal sealed class AsusAuraUsbDevice : IRgbDevice
{
    private readonly HidReportChannel _hid;
    private readonly IReadOnlyList<AuraChannel> _channels;
    private readonly bool _dryRun;
    private readonly Lock _transaction = new();
    private readonly Dictionary<string, string> _properties;
    private volatile IReadOnlyList<RgbZone> _zones;
    private bool _disposed;

    private AsusAuraUsbDevice(HidReportChannel hid, string id, string firmware, IReadOnlyList<AuraChannel> channels, bool dryRun)
    {
        _hid = hid;
        _channels = channels;
        _dryRun = dryRun;
        Id = id;

        var argbIndex = 0;
        _zones = channels.Select(channel => channel.Kind == AuraChannelKind.Mainboard
                ? new RgbZone
                {
                    Id = "mainboard",
                    Name = "Mainboard",
                    Topology = LedTopology.Linear,
                    Leds = LedLayouts.Linear(channel.LedCount),
                    MaxLedCount = channel.LedCount,
                }
                : CreateArgbZone(++argbIndex, channel.LedCount))
            .ToArray();

        _properties = new Dictionary<string, string>
        {
            ["Firmware"] = firmware,
            ["ProductId"] = $"0x{hid.Device.ProductID:X4}",
            ["vidPid"] = UsbVidPid.Format(hid.Device, AuraUsbProtocol.VendorId, hid.Device.ProductID),
            ["UsbPath"] = hid.Device.DevicePath,
            ["Channels"] = string.Join(", ", channels.Select(c => $"{c.Kind} (effect {c.EffectChannel}, direct {c.DirectChannel})")),
        };
    }

    public string Id { get; }

    public string Name { get; private init; } = "ASUS Aura USB";

    public string Vendor => "ASUS";

    public DeviceCapabilities Capabilities => DeviceCapabilities.Rgb;

    public IReadOnlyDictionary<string, string> Properties => _properties;

    public IReadOnlyList<RgbZone> Zones => _zones;

    public int MaxFramesPerSecond => 60;

    public static AsusAuraUsbDevice Open(HidDevice hidDevice, string id, int defaultArgbLeds, bool dryRun)
    {
        var hid = HidReportChannel.Open(hidDevice);
        try
        {
            hid.DrainInput();
            var firmwareReport = hid.Request(AuraUsbProtocol.FirmwareRequest(), AuraUsbProtocol.IsFirmwareResponse, 1000);
            var firmware = firmwareReport is null ? "unknown" : AuraUsbProtocol.ParseFirmware(firmwareReport);

            var config = hid.Request(AuraUsbProtocol.ConfigTableRequest(), AuraUsbProtocol.IsConfigTableResponse, 1000)
                ?? throw new IOException("The Aura controller did not return its configuration table.");
            var channels = AuraUsbProtocol.ParseChannels(config, firmware, Math.Clamp(defaultArgbLeds, 0, AuraUsbProtocol.MaxAddressableLeds));

            if (!dryRun)
            {
                hid.Write(AuraUsbProtocol.SetGen1());
                foreach (var channel in channels)
                {
                    hid.Write(AuraUsbProtocol.SetEffect(channel.EffectChannel, AuraUsbProtocol.DirectMode));
                }
            }

            var product = hidDevice.SafeProductName();
            return new AsusAuraUsbDevice(hid, id, firmware, channels, dryRun)
            {
                Name = string.IsNullOrEmpty(product) ? "ASUS Aura USB" : $"ASUS {product}",
            };
        }
        catch
        {
            hid.Dispose();
            throw;
        }
    }

    public bool TryResizeZone(string zoneId, int ledCount)
    {
        if (ledCount < 1 || ledCount > AuraUsbProtocol.MaxAddressableLeds)
        {
            return false;
        }

        var zones = _zones.ToArray();
        var index = Array.FindIndex(zones, z => z.Id == zoneId && z.IsResizable);
        if (index < 0)
        {
            return false;
        }

        zones[index] = zones[index] with { Leds = LedLayouts.Linear(ledCount) };
        _zones = zones;
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
            Span<byte> packet = stackalloc byte[AuraUsbProtocol.ReportLength];
            var offset = 0;
            for (var i = 0; i < zones.Count && offset < colors.Length; i++)
            {
                var count = Math.Min(zones[i].LedCount, colors.Length - offset);
                if (count > 0)
                {
                    var channelColors = colors.Span.Slice(offset, count);
                    var packetCount = AuraUsbProtocol.PacketCount(count);
                    for (var packetIndex = 0; packetIndex < packetCount; packetIndex++)
                    {
                        AuraUsbProtocol.WriteDirectPacket(_channels[i].DirectChannel, channelColors, packetIndex, packet);
                        _hid.Write(packet);
                    }
                }

                offset += zones[i].LedCount;
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

    private static RgbZone CreateArgbZone(int number, int ledCount) => new()
    {
        Id = $"argb{number}",
        Name = $"ARGB header {number}",
        Topology = LedTopology.Linear,
        Leds = LedLayouts.Linear(ledCount),
        IsResizable = true,
        MaxLedCount = AuraUsbProtocol.MaxAddressableLeds,
    };
}
