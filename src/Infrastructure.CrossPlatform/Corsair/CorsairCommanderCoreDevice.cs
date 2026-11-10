using HidSharp;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Hid;

namespace RigForge.Infrastructure.CrossPlatform.Corsair;

/// <summary>Commander Core, Core XT and iCUE Commander Core in volatile direct RGB mode.</summary>
internal sealed class CorsairCommanderCoreDevice : IRgbDevice
{
    private readonly HidReportChannel _hid;
    private readonly int _reportLength;
    private readonly bool _dryRun;
    private readonly Lock _transaction = new();
    private readonly Dictionary<string, string> _properties;
    private volatile IReadOnlyList<RgbZone> _zones;
    private bool _disposed;

    internal CorsairCommanderCoreDevice(
        HidReportChannel hid,
        string id,
        string name,
        string usbPath,
        int productId,
        int reportLength,
        int ledCount,
        bool dryRun)
    {
        _hid = hid;
        _reportLength = reportLength;
        _dryRun = dryRun;
        Id = id;
        Name = name;
        var leds = Math.Clamp(ledCount, 1, CorsairProtocol.MaxCommanderCoreLeds);
        _zones =
        [
            new RgbZone
            {
                Id = "rgb",
                Name = "RGB",
                Topology = LedTopology.Linear,
                Leds = LedLayouts.Linear(leds),
                IsResizable = true,
                MaxLedCount = CorsairProtocol.MaxCommanderCoreLeds,
            },
        ];
        _properties = new Dictionary<string, string>
        {
            ["ProductId"] = $"0x{productId:X4}",
            ["vidPid"] = UsbVidPid.Format(hid.Device, CorsairProtocol.VendorId, productId),
            ["UsbPath"] = usbPath,
            ["ReportLength"] = reportLength.ToString(),
        };
    }

    public string Id { get; }

    public string Name { get; }

    public string Vendor => "Corsair";

    public DeviceCapabilities Capabilities => DeviceCapabilities.Rgb;

    public IReadOnlyDictionary<string, string> Properties => _properties;

    public IReadOnlyList<RgbZone> Zones => _zones;

    public int MaxFramesPerSecond => 30;

    public static CorsairCommanderCoreDevice Open(HidDevice hidDevice, string id, int ledCount, bool dryRun)
    {
        var length = hidDevice.GetMaxOutputReportLength();
        if (length < CorsairProtocol.CommanderCoreMinReportLength)
        {
            throw new IOException($"Commander Core report length {length} is too small.");
        }

        if (length > CorsairProtocol.CommanderCoreLongReportLength)
        {
            length = CorsairProtocol.CommanderCoreLongReportLength;
        }

        var hid = HidReportChannel.Open(hidDevice);
        try
        {
            if (!dryRun)
            {
                Span<byte> packet = stackalloc byte[length];
                CorsairProtocol.WriteCommanderCoreSoftwareMode(packet);
                hid.Write(packet);
            }

            var product = hidDevice.SafeProductName();
            return new CorsairCommanderCoreDevice(
                hid,
                id,
                product.Length > 0 ? product : "Corsair Commander Core",
                hidDevice.DevicePath,
                hidDevice.ProductID,
                length,
                ledCount,
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
        if (_dryRun || colors.IsEmpty)
        {
            return ValueTask.CompletedTask;
        }

        var count = Math.Min(_zones[0].LedCount, colors.Length);
        if (count <= 0)
        {
            return ValueTask.CompletedTask;
        }

        lock (_transaction)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Span<byte> packet = stackalloc byte[_reportLength];
            var frame = colors.Span[..count];
            CorsairProtocol.WriteCommanderCoreOpenEndpoint(packet);
            _hid.Write(packet);
            var packetCount = CorsairProtocol.CommanderCoreRgbPacketCount(count, _reportLength);
            for (var packetIndex = 0; packetIndex < packetCount; packetIndex++)
            {
                CorsairProtocol.WriteCommanderCoreRgbPacket(frame, packetIndex, _reportLength, packet);
                _hid.Write(packet);
            }

            CorsairProtocol.WriteCommanderCoreCloseEndpoint(packet);
            _hid.Write(packet);
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
                    Span<byte> packet = stackalloc byte[_reportLength];
                    CorsairProtocol.WriteCommanderCoreHardwareMode(packet);
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
}
