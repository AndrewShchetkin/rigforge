using HidSharp;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Hid;

namespace RigForge.Infrastructure.CrossPlatform.LianLi;

/// <summary>Lian Li UNI HUB: four resizable channels in volatile direct mode.</summary>
internal sealed class LianLiUniHubDevice : IRgbDevice
{
    private readonly HidReportChannel _hid;
    private readonly LianLiColorOrder _order;
    private readonly int _colorReportLength;
    private readonly bool _dryRun;
    private readonly Lock _transaction = new();
    private readonly Dictionary<string, string> _properties;
    private volatile IReadOnlyList<RgbZone> _zones;
    private bool _disposed;

    internal LianLiUniHubDevice(
        HidReportChannel hid,
        string id,
        string name,
        string usbPath,
        int productId,
        LianLiColorOrder order,
        int colorReportLength,
        int ledsPerChannel,
        int maxLeds,
        bool dryRun)
    {
        _hid = hid;
        _order = order;
        _colorReportLength = colorReportLength;
        _dryRun = dryRun;
        Id = id;
        Name = name;
        var leds = Math.Clamp(ledsPerChannel, 1, maxLeds);
        var zones = new RgbZone[LianLiUniHubProtocol.ChannelCount];
        for (var channel = 0; channel < zones.Length; channel++)
        {
            zones[channel] = new RgbZone
            {
                Id = $"channel{channel + 1}",
                Name = $"Channel {channel + 1}",
                Topology = LedTopology.Linear,
                Leds = LedLayouts.Linear(leds),
                IsResizable = true,
                MaxLedCount = maxLeds,
            };
        }

        _zones = zones;
        _properties = new Dictionary<string, string>
        {
            ["ProductId"] = $"0x{productId:X4}",
            ["vidPid"] = UsbVidPid.Format(hid.Device, LianLiUniHubProtocol.VendorId, productId),
            ["UsbPath"] = usbPath,
            ["ColorOrder"] = order.ToString(),
        };
    }

    public string Id { get; }

    public string Name { get; }

    public string Vendor => "Lian Li";

    public DeviceCapabilities Capabilities => DeviceCapabilities.Rgb;

    public IReadOnlyDictionary<string, string> Properties => _properties;

    public IReadOnlyList<RgbZone> Zones => _zones;

    public int MaxFramesPerSecond => 20;

    public static LianLiUniHubDevice Open(HidDevice hidDevice, string id, bool dryRun)
    {
        var output = hidDevice.GetMaxOutputReportLength();
        if (output < LianLiUniHubProtocol.ReportLength)
        {
            throw new IOException($"UNI HUB report length {output} is too small.");
        }

        var colorLength = output >= 353 ? 353 : LianLiUniHubProtocol.ReportLength;
        var hid = HidReportChannel.Open(hidDevice);
        try
        {
            var product = hidDevice.SafeProductName();
            return new LianLiUniHubDevice(
                hid,
                id,
                product.Length > 0 ? product : "Lian Li UNI HUB",
                hidDevice.DevicePath,
                hidDevice.ProductID,
                LianLiUniHubProtocol.ColorOrder(hidDevice.ProductID),
                colorLength,
                LianLiUniHubProtocol.DefaultLedsPerChannel,
                LianLiUniHubProtocol.MaxLedsPerChannel(hidDevice.ProductID),
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
            var bufferLength = Math.Max(_colorReportLength, LianLiUniHubProtocol.ReportLength);
            Span<byte> packet = stackalloc byte[bufferLength];
            var zones = _zones;
            var offset = 0;
            for (var channel = 0; channel < zones.Count && offset < colors.Length; channel++)
            {
                var count = Math.Min(zones[channel].LedCount, colors.Length - offset);
                if (count > 0)
                {
                    var slice = colors.Span.Slice(offset, count);
                    LianLiUniHubProtocol.WriteModeHeader((byte)channel, count, packet[..LianLiUniHubProtocol.ReportLength]);
                    _hid.Write(packet[..LianLiUniHubProtocol.ReportLength]);
                    var packetCount = LianLiUniHubProtocol.ColorPacketCount(count, _colorReportLength);
                    for (var packetIndex = 0; packetIndex < packetCount; packetIndex++)
                    {
                        LianLiUniHubProtocol.WriteColorPacket((byte)channel, packetIndex, slice, _order, _colorReportLength, packet[.._colorReportLength]);
                        _hid.Write(packet[.._colorReportLength]);
                    }

                    LianLiUniHubProtocol.WriteStaticApply((byte)channel, packet[..LianLiUniHubProtocol.ReportLength]);
                    _hid.Write(packet[..LianLiUniHubProtocol.ReportLength]);
                }

                offset += zones[channel].LedCount;
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
