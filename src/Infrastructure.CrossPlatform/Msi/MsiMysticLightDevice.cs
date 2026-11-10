using HidSharp;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Hid;

namespace RigForge.Infrastructure.CrossPlatform.Msi;

internal enum MsiReportKind
{
    Zones185,
    ArgbStream,
}

/// <summary>MSI Mystic Light motherboard controller. The 185-byte report never sets the flash-save byte.</summary>
internal sealed class MsiMysticLightDevice : IRgbDevice
{
    private readonly HidReportChannel _hid;
    private readonly MsiReportKind _kind;
    private readonly byte[] _streamZones;
    private readonly bool _dryRun;
    private readonly Lock _transaction = new();
    private readonly Dictionary<string, string> _properties;
    private volatile IReadOnlyList<RgbZone> _zones;
    private bool _disposed;

    internal MsiMysticLightDevice(
        HidReportChannel hid,
        string id,
        string name,
        string usbPath,
        int productId,
        MsiReportKind kind,
        bool dryRun)
    {
        _hid = hid;
        _kind = kind;
        _dryRun = dryRun;
        Id = id;
        Name = name;
        if (kind == MsiReportKind.Zones185)
        {
            _streamZones = [];
            _zones =
            [
                Fixed("onboard", "Onboard"),
                Fixed("jrgb1", "JRGB1"),
                Fixed("jrgb2", "JRGB2"),
                Fixed("jrainbow1", "JRAINBOW1"),
                Fixed("jrainbow2", "JRAINBOW2"),
            ];
        }
        else
        {
            _streamZones =
            [
                MsiMysticLightProtocol.ZoneOnboard,
                MsiMysticLightProtocol.ZoneJrgb1,
                MsiMysticLightProtocol.ZoneJrgb2,
                MsiMysticLightProtocol.ZoneJrainbow1,
                MsiMysticLightProtocol.ZoneJrainbow2,
            ];
            _zones =
            [
                Fixed("onboard", "Onboard"),
                Fixed("jrgb1", "JRGB1"),
                Fixed("jrgb2", "JRGB2"),
                Resizable("jrainbow1", "JRAINBOW1"),
                Resizable("jrainbow2", "JRAINBOW2"),
            ];
        }

        _properties = new Dictionary<string, string>
        {
            ["ProductId"] = $"0x{productId:X4}",
            ["vidPid"] = UsbVidPid.Format(hid.Device, MsiMysticLightProtocol.VendorId, productId),
            ["UsbPath"] = usbPath,
            ["Report"] = kind == MsiReportKind.Zones185 ? "185" : "64",
        };
    }

    public string Id { get; }

    public string Name { get; }

    public string Vendor => "MSI";

    public DeviceCapabilities Capabilities => DeviceCapabilities.Rgb;

    public IReadOnlyDictionary<string, string> Properties => _properties;

    public IReadOnlyList<RgbZone> Zones => _zones;

    public int MaxFramesPerSecond => 20;

    public static MsiMysticLightDevice Open(HidDevice hidDevice, string id, MsiReportKind kind, bool dryRun)
    {
        var length = kind == MsiReportKind.Zones185
            ? MsiMysticLightProtocol.ReportLength
            : MsiMysticLightProtocol.StreamReportLength;
        var hid = HidReportChannelFactory.OpenOutputOrFeature(hidDevice, length);
        try
        {
            var product = hidDevice.SafeProductName();
            return new MsiMysticLightDevice(
                hid,
                id,
                product.Length > 0 ? product : "MSI Mystic Light",
                hidDevice.DevicePath,
                hidDevice.ProductID,
                kind,
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
            if (_kind == MsiReportKind.Zones185)
            {
                Write185(colors.Span);
            }
            else
            {
                WriteStream(colors.Span);
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

    private void Write185(ReadOnlySpan<RgbColor> colors)
    {
        Span<RgbColor> zoneColors = stackalloc RgbColor[5];
        var zones = _zones;
        var filled = 0;
        var offset = 0;
        for (var i = 0; i < zones.Count && filled < zoneColors.Length && offset < colors.Length; i++)
        {
            zoneColors[filled++] = colors[offset];
            offset += zones[i].LedCount;
        }

        Span<byte> report = stackalloc byte[MsiMysticLightProtocol.ReportLength];
        MsiMysticLightProtocol.Write185Report(zoneColors[..filled], report);
        _hid.Write(report);
    }

    private void WriteStream(ReadOnlySpan<RgbColor> colors)
    {
        Span<byte> packet = stackalloc byte[MsiMysticLightProtocol.StreamReportLength];
        var zones = _zones;
        var offset = 0;
        for (var i = 0; i < zones.Count && offset < colors.Length; i++)
        {
            var count = Math.Min(zones[i].LedCount, colors.Length - offset);
            if (count > 0)
            {
                var slice = colors.Slice(offset, count);
                var packetCount = MsiMysticLightProtocol.StreamPacketCount(count);
                for (var packetIndex = 0; packetIndex < packetCount; packetIndex++)
                {
                    MsiMysticLightProtocol.WriteArgbStream(_streamZones[i], packetIndex, slice, packet);
                    _hid.Write(packet);
                }
            }

            offset += zones[i].LedCount;
        }
    }

    private static RgbZone Fixed(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Topology = LedTopology.Linear,
        Leds = LedLayouts.Linear(1),
        MaxLedCount = 1,
    };

    private static RgbZone Resizable(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Topology = LedTopology.Linear,
        Leds = LedLayouts.Linear(MsiMysticLightProtocol.DefaultArgbLeds),
        IsResizable = true,
        MaxLedCount = MsiMysticLightProtocol.MaxArgbLeds,
    };
}
