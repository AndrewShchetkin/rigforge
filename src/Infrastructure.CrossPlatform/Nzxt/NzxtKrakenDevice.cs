using HidSharp;
using Microsoft.Extensions.Logging;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Hid;
using RigForge.Infrastructure.CrossPlatform.Usb;

namespace RigForge.Infrastructure.CrossPlatform.Nzxt;

/// <summary>
/// NZXT Kraken Z3 / 2023 / 2024: liquid temperature, pump and radiator fan control over HID,
/// and the pump-head screen over USB bulk.
/// </summary>
internal sealed partial class NzxtKrakenDevice : IFanController, ISensorProvider, IAioLcdDevice, IRgbDevice
{
    private const byte BulkEndpoint = 0x02;
    private const int PumpMinimumDuty = 20;

    // Profiles left in the firmware when the daemon gives up control: liquid temperature driven.
    private static readonly (int Temperature, int Duty)[] PumpFallbackCurve = [(20, 50), (30, 60), (35, 75), (40, 90), (45, 100)];
    private static readonly (int Temperature, int Duty)[] FanFallbackCurve = [(20, 30), (30, 40), (35, 55), (40, 75), (45, 100)];

    // Added under host-side fixed duties so the firmware still ramps up on hot coolant if the daemon dies.
    private static readonly (int Temperature, int Duty)[] SafetyFloor = [(20, 0), (44, 0), (50, 70), (55, 100)];

    private readonly HidReportChannel _hid;
    private readonly KrakenModel _model;
    private readonly KrakenLcdUploader? _lcd;
    private readonly bool _dryRun;
    private readonly Lock _transaction = new();
    private readonly Dictionary<string, string> _properties;
    private readonly string _liquidId;
    private readonly string _pumpRpmId;
    private readonly string _pumpDutyId;
    private readonly string _fanRpmId;
    private readonly string _fanDutyId;
    private readonly int _orientation;
    private volatile IReadOnlyList<RgbZone> _zones = [];
    private bool _disposed;

    internal NzxtKrakenDevice(HidReportChannel hid, KrakenModel model, string id, string firmware, int orientation, KrakenLcdUploader? lcd, bool dryRun)
    {
        _hid = hid;
        _model = model;
        _lcd = lcd;
        _dryRun = dryRun;
        _orientation = orientation;
        Id = id;

        _liquidId = $"{id}/liquid";
        _pumpRpmId = $"{id}/pump-rpm";
        _pumpDutyId = $"{id}/pump-duty";
        _fanRpmId = $"{id}/fan-rpm";
        _fanDutyId = $"{id}/fan-duty";

        Channels =
        [
            new FanChannel("pump", "Pump", FanChannelKind.Pump, PumpMinimumDuty, 100, _pumpRpmId),
            new FanChannel("fan", "Radiator fans", FanChannelKind.Fan, 0, 100, _fanRpmId),
        ];

        Lcd = new LcdInfo(model.LcdWidth, model.LcdHeight, IsCircular: true, MaxFramesPerSecond: lcd?.UsesRgb565Stream == true ? 15f : 1f);

        _properties = new Dictionary<string, string>
        {
            ["Firmware"] = firmware,
            ["ProductId"] = $"0x{model.ProductId:X4}",
            ["vidPid"] = UsbVidPid.Format(hid.Device, NzxtCatalog.VendorId, model.ProductId),
            ["UsbPath"] = hid.Device?.DevicePath ?? string.Empty,
            ["Screen"] = lcd is null ? "unavailable" : $"{model.LcdWidth}x{model.LcdHeight} ({(lcd.UsesRgb565Stream ? "RGB565 stream" : "bucket upload")})",
        };

        _zones = model.RgbRingLeds > 0
            ?
            [
                new RgbZone
                {
                    Id = "ring",
                    Name = "Ring",
                    Topology = LedTopology.Ring,
                    Leds = LedLayouts.Ring(Math.Clamp(model.RgbRingLeds, 1, NzxtProtocol.MaxLedsPerChannel)),
                    IsResizable = true,
                    MaxLedCount = NzxtProtocol.MaxLedsPerChannel,
                },
            ]
            : [];
    }

    public string Id { get; }

    public string Name => _model.Name;

    public string Vendor => "NZXT";

    public DeviceCapabilities Capabilities =>
        DeviceCapabilities.FanControl
        | DeviceCapabilities.Sensors
        | (_lcd is null ? DeviceCapabilities.None : DeviceCapabilities.Lcd)
        | (_zones.Count > 0 ? DeviceCapabilities.Rgb : DeviceCapabilities.None);

    public IReadOnlyDictionary<string, string> Properties => _properties;

    public IReadOnlyList<RgbZone> Zones => _zones;

    public int MaxFramesPerSecond => 30;

    public IReadOnlyList<FanChannel> Channels { get; }

    public LcdInfo Lcd { get; }

    public static NzxtKrakenDevice Open(
        HidDevice hidDevice,
        KrakenModel model,
        string id,
        IUsbBulkTransportFactory bulkFactory,
        NzxtOptions options,
        bool dryRun,
        ILogger logger)
    {
        var hid = HidReportChannel.Open(hidDevice);
        try
        {
            hid.DrainInput();
            hid.Write(NzxtProtocol.KrakenSetStatusInterval());
            hid.Write(NzxtProtocol.KrakenFinishInitialization());

            var firmwareReport = hid.Request(NzxtProtocol.FirmwareRequest(), r => NzxtProtocol.IsResponse(r, 0x11, 0x01), 1000);
            var firmware = firmwareReport is null ? "unknown" : NzxtProtocol.ParseFirmware(firmwareReport);
            var lcdReport = hid.Request(NzxtProtocol.LcdInfoRequest(), r => NzxtProtocol.IsResponse(r, 0x31, 0x01), 1000);
            var (_, orientation) = lcdReport is null ? (50, 0) : NzxtProtocol.ParseLcdInfo(lcdReport);

            KrakenLcdUploader? lcd = null;
            if (options.EnableLcd)
            {
                var bulk = bulkFactory.TryOpen(NzxtCatalog.VendorId, model.ProductId, hidDevice.SafeSerialNumber(), BulkEndpoint);
                if (bulk is not null)
                {
                    // liquidctl: only the Kraken 2023 (0x300E) on firmware 2.x takes RGB565 framebuffer writes.
                    var rgb565 = model.ProductId == 0x300E && firmware.StartsWith("2.", StringComparison.Ordinal);
                    lcd = new KrakenLcdUploader(hid, bulk, model.LcdWidth, model.LcdHeight, rgb565, logger);
                    if (model.LcdExperimental)
                    {
                        LogExperimentalScreen(logger, model.Name);
                    }
                }
            }

            return new NzxtKrakenDevice(hid, model, id, firmware, orientation, lcd, dryRun);
        }
        catch
        {
            hid.Dispose();
            throw;
        }
    }

    public ValueTask<IReadOnlyList<SensorReading>> ReadSensorsAsync(CancellationToken cancellationToken)
    {
        KrakenStatus? status;
        lock (_transaction)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _hid.DrainInput();
            var report = _hid.Request(NzxtProtocol.KrakenStatusRequest(), r => r.Length >= 26 && r[0] == 0x75, 600);
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
        var (header, minimum) = ResolveChannel(channelId);
        var duties = NzxtProtocol.FixedDutyWithSafetyFloor(Math.Clamp(dutyPercent, minimum, 100), SafetyFloor);
        WriteProfile(header, duties);
        return ValueTask.CompletedTask;
    }

    public ValueTask RestoreDefaultAsync(string channelId, CancellationToken cancellationToken)
    {
        var (header, minimum) = ResolveChannel(channelId);
        var curve = channelId == "pump" ? PumpFallbackCurve : FanFallbackCurve;
        WriteProfile(header, NzxtProtocol.InterpolateProfile(curve, minimum, 100));
        return ValueTask.CompletedTask;
    }

    public ValueTask PushFrameAsync(ReadOnlyMemory<byte> rgba, CancellationToken cancellationToken)
    {
        if (_lcd is null || _dryRun)
        {
            return ValueTask.CompletedTask;
        }

        lock (_transaction)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _lcd.Upload(rgba.Span);
        }

        return ValueTask.CompletedTask;
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

        var zone = copy[index];
        copy[index] = zone with
        {
            Leds = zone.Topology == LedTopology.Ring ? LedLayouts.Ring(ledCount) : LedLayouts.Linear(ledCount),
        };
        _zones = copy;
        return true;
    }

    public ValueTask UpdateLedsAsync(ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken)
    {
        if (_dryRun || _zones.Count == 0 || colors.IsEmpty)
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
            Span<byte> packet = stackalloc byte[NzxtProtocol.ReportLength];
            var frame = colors.Span[..count];
            var packetCount = NzxtProtocol.Hue2PacketCount(count);
            for (var packetIndex = 0; packetIndex < packetCount; packetIndex++)
            {
                NzxtProtocol.WriteHue2Packet(NzxtProtocol.KrakenEliteRingChannel, packetIndex, frame, packet);
                _hid.Write(packet);
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask SetBrightnessAsync(int percent, CancellationToken cancellationToken)
    {
        if (_dryRun)
        {
            return ValueTask.CompletedTask;
        }

        lock (_transaction)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _hid.Write(NzxtProtocol.LcdSettings(percent, _orientation));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ShowBuiltInScreenAsync(CancellationToken cancellationToken)
    {
        if (_lcd is null || _dryRun)
        {
            return ValueTask.CompletedTask;
        }

        lock (_transaction)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _lcd.ShowLiquidScreen();
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
            _lcd?.Dispose();
            _hid.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private (byte[] Header, int Minimum) ResolveChannel(string channelId) => channelId switch
    {
        "pump" => (_model.PumpHeader, PumpMinimumDuty),
        "fan" => (_model.FanHeader, 0),
        _ => throw new ArgumentException($"Unknown channel '{channelId}'.", nameof(channelId)),
    };

    private void WriteProfile(byte[] header, byte[] duties)
    {
        if (_dryRun)
        {
            return;
        }

        lock (_transaction)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _hid.Write(NzxtProtocol.KrakenDutyProfile(header, duties));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Model}: screen uploads are reported as unreliable by liquidctl; treat LCD output as experimental")]
    private static partial void LogExperimentalScreen(ILogger logger, string model);
}
