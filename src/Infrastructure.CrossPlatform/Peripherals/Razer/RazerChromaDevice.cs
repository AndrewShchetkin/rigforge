using HidSharp;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Hid;

namespace RigForge.Infrastructure.CrossPlatform.Peripherals.Razer;

/// <summary>
/// Razer Chroma HID device. Each frame is a custom matrix plus a volatile effect-apply.
/// Nothing in this driver writes onboard profile memory.
/// </summary>
internal sealed class RazerChromaDevice : IRgbDevice
{
    private readonly HidReportChannel _hid;
    private readonly int _reportLength;
    private readonly bool _dryRun;
    private readonly Lock _transaction = new();
    private readonly Dictionary<string, string> _properties;
    private volatile IReadOnlyList<RgbZone> _zones;
    private bool _disposed;

    private RazerChromaDevice(HidReportChannel hid, string id, string name, string usbPath, int productId, int reportLength, IReadOnlyList<RgbZone> zones, bool dryRun)
    {
        _hid = hid;
        _reportLength = reportLength;
        _dryRun = dryRun;
        _zones = zones;
        Id = id;
        Name = name;
        _properties = new Dictionary<string, string>
        {
            ["ProductId"] = $"0x{productId:X4}",
            ["vidPid"] = UsbVidPid.Format(hid.Device, RazerChromaProtocol.VendorId, productId),
            ["UsbPath"] = usbPath,
        };
    }

    public string Id { get; }

    public string Name { get; }

    public string Vendor => "Razer";

    public DeviceCapabilities Capabilities => DeviceCapabilities.Rgb;

    public IReadOnlyDictionary<string, string> Properties => _properties;

    public IReadOnlyList<RgbZone> Zones => _zones;

    public int MaxFramesPerSecond => 40;

    public static bool IsChromaInterface(HidDevice device)
    {
        var output = SafeLength(() => device.GetMaxOutputReportLength());
        if (output < RazerChromaProtocol.PayloadLength)
        {
            return false;
        }

        var usages = device.SafeTopLevelUsages();
        if (usages.Count == 0)
        {
            return true;
        }

        foreach (var packed in usages)
        {
            if ((packed >> 16) >= 0xFF00)
            {
                return true;
            }
        }

        return false;
    }

    public static RazerChromaDevice Open(HidDevice hidDevice, string id, bool dryRun)
    {
        var output = SafeLength(() => hidDevice.GetMaxOutputReportLength());
        var reportLength = output >= RazerChromaProtocol.ReportLength ? RazerChromaProtocol.ReportLength : RazerChromaProtocol.PayloadLength;
        var hid = HidReportChannel.Open(hidDevice);
        try
        {
            var product = hidDevice.SafeProductName();
            var zones = CreateZones(hidDevice.ProductID);
            return new RazerChromaDevice(
                hid,
                id,
                product.Length > 0 ? product : DefaultName(hidDevice.ProductID),
                hidDevice.DevicePath,
                hidDevice.ProductID,
                reportLength,
                zones,
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
        cancellationToken.ThrowIfCancellationRequested();
        if (_dryRun || colors.IsEmpty)
        {
            return ValueTask.CompletedTask;
        }

        lock (_transaction)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Span<byte> storage = stackalloc byte[RazerChromaProtocol.ReportLength];
            var report = storage[.._reportLength];
            var span = colors.Span;
            var zones = _zones;
            var offset = 0;
            for (var row = 0; row < zones.Count && offset < span.Length; row++)
            {
                var count = Math.Min(zones[row].LedCount, span.Length - offset);
                var sent = 0;
                while (sent < count)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var chunk = Math.Min(RazerChromaProtocol.MaxLedsPerFrame, count - sent);
                    var slice = span.Slice(offset + sent, chunk);
                    RazerChromaProtocol.WriteMatrixCustomFrame((byte)row, (byte)sent, slice, report);
                    _hid.Write(report);
                    sent += chunk;
                }

                offset += zones[row].LedCount;
            }

            RazerChromaProtocol.WriteStaticEffect(report);
            _hid.Write(report);
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

    private static IReadOnlyList<RgbZone> CreateZones(int productId)
    {
        if (productId == RazerChromaProtocol.ArgbControllerProductId)
        {
            var ports = new RgbZone[RazerChromaProtocol.ArgbPortCount];
            for (var port = 0; port < ports.Length; port++)
            {
                ports[port] = new RgbZone
                {
                    Id = $"port{port + 1}",
                    Name = $"ARGB port {port + 1}",
                    Topology = LedTopology.Linear,
                    Leds = LedLayouts.Linear(RazerChromaProtocol.DefaultArgbLedsPerPort),
                    IsResizable = true,
                    MaxLedCount = RazerChromaProtocol.MaxArgbLedsPerPort,
                };
            }

            return ports;
        }

        if (productId == 0x0C00)
        {
            return [LinearZone("pad", "Firefly", 15)];
        }

        if (RazerChromaProtocol.IsKeyboard(productId))
        {
            var leds = productId == 0x0258 ? 61 : 90;
            return [LinearZone("matrix", "Keyboard", leds)];
        }

        return
        [
            new RgbZone
            {
                Id = "primary",
                Name = "Primary Zone",
                Topology = LedTopology.Point,
                Leds = LedLayouts.Point(),
                MaxLedCount = 1,
            },
            new RgbZone
            {
                Id = "logo",
                Name = "Logo Zone",
                Topology = LedTopology.Point,
                Leds = LedLayouts.Point(),
                MaxLedCount = 1,
            },
        ];
    }

    private static RgbZone LinearZone(string id, string name, int leds) => new()
    {
        Id = id,
        Name = name,
        Topology = LedTopology.Linear,
        Leds = LedLayouts.Linear(leds),
        IsResizable = false,
        MaxLedCount = leds,
    };

    private static string DefaultName(int productId) => productId switch
    {
        RazerChromaProtocol.ArgbControllerProductId => "Razer Chroma ARGB Controller",
        0x0C00 => "Razer Firefly",
        _ => "Razer Chroma",
    };

    private static int SafeLength(Func<int> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
