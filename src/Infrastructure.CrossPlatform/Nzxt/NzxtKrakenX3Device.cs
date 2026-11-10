using HidSharp;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Hid;

namespace RigForge.Infrastructure.CrossPlatform.Nzxt;

/// <summary>
/// NZXT Kraken X3 (X53/X63/X73, PIDs 0x2007 and 0x2014): ring and logo RGB, pump duty with a firmware safety floor,
/// and liquid / pump / fan telemetry. Direct RGB only — nothing is written to flash.
/// </summary>
internal sealed class NzxtKrakenX3Device : IRgbDevice, IFanController, ISensorProvider
{
    private const int PumpMinimumDuty = 20;

    private static readonly byte[] PumpHeader = [0x01, 0x00, 0x00];
    private static readonly (int Temperature, int Duty)[] PumpFallbackCurve = [(20, 50), (30, 60), (35, 75), (40, 90), (45, 100)];
    private static readonly (int Temperature, int Duty)[] SafetyFloor = [(20, 0), (44, 0), (50, 70), (55, 100)];

    private readonly HidReportChannel _hid;
    private readonly bool _dryRun;
    private readonly Lock _transaction = new();
    private readonly Dictionary<string, string> _properties;
    private readonly string _liquidId;
    private readonly string _pumpRpmId;
    private readonly string _pumpDutyId;
    private readonly string _fanRpmId;
    private readonly string _fanDutyId;
    private readonly byte[] _zoneChannels;
    private volatile IReadOnlyList<RgbZone> _zones;
    private bool _disposed;

    internal NzxtKrakenX3Device(HidReportChannel hid, KrakenX3Model model, string id, string firmware, bool dryRun)
    {
        _hid = hid;
        _dryRun = dryRun;
        Id = id;
        Name = model.Name;
        _liquidId = $"{id}/liquid";
        _pumpRpmId = $"{id}/pump-rpm";
        _pumpDutyId = $"{id}/pump-duty";
        _fanRpmId = $"{id}/fan-rpm";
        _fanDutyId = $"{id}/fan-duty";
        _zoneChannels = [NzxtProtocol.KrakenX3RingChannel, NzxtProtocol.KrakenX3LogoChannel];
        _zones =
        [
            new RgbZone
            {
                Id = "ring",
                Name = "Ring",
                Topology = LedTopology.Ring,
                Leds = LedLayouts.Ring(NzxtProtocol.KrakenX3RingLeds),
                MaxLedCount = NzxtProtocol.KrakenX3RingLeds,
            },
            new RgbZone
            {
                Id = "logo",
                Name = "Logo",
                Topology = LedTopology.Point,
                Leds = LedLayouts.Point(),
                MaxLedCount = NzxtProtocol.KrakenX3LogoLeds,
            },
        ];
        Channels = [new FanChannel("pump", "Pump", FanChannelKind.Pump, PumpMinimumDuty, 100, _pumpRpmId)];
        _properties = new Dictionary<string, string>
        {
            ["Firmware"] = firmware,
            ["ProductId"] = $"0x{model.ProductId:X4}",
            ["vidPid"] = UsbVidPid.Format(hid.Device, NzxtCatalog.VendorId, model.ProductId),
            ["UsbPath"] = hid.Device?.DevicePath ?? string.Empty,
        };
    }

    public string Id { get; }

    public string Name { get; }

    public string Vendor => "NZXT";

    public DeviceCapabilities Capabilities => DeviceCapabilities.Rgb | DeviceCapabilities.FanControl | DeviceCapabilities.Sensors;

    public IReadOnlyDictionary<string, string> Properties => _properties;

    public IReadOnlyList<RgbZone> Zones => _zones;

    public int MaxFramesPerSecond => 30;

    public IReadOnlyList<FanChannel> Channels { get; }

    public static NzxtKrakenX3Device Open(HidDevice hidDevice, KrakenX3Model model, string id, bool dryRun)
    {
        var hid = HidReportChannel.Open(hidDevice);
        try
        {
            hid.DrainInput();
            hid.Write(NzxtProtocol.KrakenSetStatusInterval());
            hid.Write(NzxtProtocol.KrakenFinishInitialization());
            var firmwareReport = hid.Request(NzxtProtocol.FirmwareRequest(), report => NzxtProtocol.IsResponse(report, 0x11, 0x01), 1000);
            var firmware = firmwareReport is null ? "unknown" : NzxtProtocol.ParseFirmware(firmwareReport);
            var product = hidDevice.SafeProductName();
            return new NzxtKrakenX3Device(hid, model with { Name = product.Length > 0 ? product : model.Name }, id, firmware, dryRun);
        }
        catch
        {
            hid.Dispose();
            throw;
        }
    }

    public bool TryResizeZone(string zoneId, int ledCount) => false;

    public ValueTask UpdateLedsAsync(ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken)
    {
        if (_dryRun || colors.IsEmpty)
        {
            return ValueTask.CompletedTask;
        }

        lock (_transaction)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Span<byte> packet = stackalloc byte[NzxtProtocol.ReportLength];
            var zones = _zones;
            var offset = 0;
            for (var i = 0; i < zones.Count && offset < colors.Length; i++)
            {
                var count = Math.Min(zones[i].LedCount, colors.Length - offset);
                if (count > 0)
                {
                    var slice = colors.Span.Slice(offset, count);
                    var packetCount = NzxtProtocol.KrakenX3PacketCount(count);
                    for (var packetIndex = 0; packetIndex < packetCount; packetIndex++)
                    {
                        NzxtProtocol.WriteKrakenX3RgbPacket(_zoneChannels[i], packetIndex, slice, packet);
                        _hid.Write(packet);
                    }
                }

                offset += zones[i].LedCount;
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<SensorReading>> ReadSensorsAsync(CancellationToken cancellationToken)
    {
        KrakenStatus? status;
        lock (_transaction)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _hid.DrainInput();
            var report = _hid.Request(NzxtProtocol.KrakenStatusRequest(), report => report.Length >= 26 && report[0] == 0x75, 600);
            status = report is null ? null : NzxtProtocol.ParseKrakenStatus(report);
        }

        if (status is null)
        {
            throw new IOException($"{Name} did not return a valid status report.");
        }

        IReadOnlyList<SensorReading> readings =
        [
            new(_liquidId, "Liquid temperature", SensorKind.Temperature, status.LiquidTemperature) { Role = SensorRole.LiquidTemperature },
            new(_pumpRpmId, "Pump speed", SensorKind.FanSpeed, status.PumpRpm) { Role = SensorRole.PumpSpeed },
            new(_pumpDutyId, "Pump duty", SensorKind.Duty, status.PumpDuty),
            new(_fanRpmId, "Radiator fan speed", SensorKind.FanSpeed, status.FanRpm),
            new(_fanDutyId, "Radiator fan duty", SensorKind.Duty, status.FanDuty),
        ];
        return ValueTask.FromResult(readings);
    }

    public ValueTask SetDutyAsync(string channelId, int dutyPercent, CancellationToken cancellationToken)
    {
        if (channelId != "pump")
        {
            throw new ArgumentException($"Unknown channel '{channelId}'.", nameof(channelId));
        }

        var duty = Math.Clamp(dutyPercent, PumpMinimumDuty, 100);
        WriteProfile(NzxtProtocol.FixedDutyWithSafetyFloor(duty, SafetyFloor));
        return ValueTask.CompletedTask;
    }

    public ValueTask RestoreDefaultAsync(string channelId, CancellationToken cancellationToken)
    {
        if (channelId != "pump")
        {
            throw new ArgumentException($"Unknown channel '{channelId}'.", nameof(channelId));
        }

        WriteProfile(NzxtProtocol.InterpolateProfile(PumpFallbackCurve, PumpMinimumDuty, 100));
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

    private void WriteProfile(byte[] duties)
    {
        if (_dryRun)
        {
            return;
        }

        lock (_transaction)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _hid.Write(NzxtProtocol.KrakenDutyProfile(PumpHeader, duties));
        }
    }
}
