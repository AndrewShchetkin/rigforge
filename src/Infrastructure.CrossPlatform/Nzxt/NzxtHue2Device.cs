using HidSharp;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Hid;

namespace RigForge.Infrastructure.CrossPlatform.Nzxt;

/// <summary>
/// HUE 2 generation controllers: RGB &amp; Fan Controller, RGB Controller (bundled with Kraken RGB / H-series cases),
/// Smart Device V2, HUE 2. LED channels are driven in direct mode; LED counts come from the accessories the
/// controller detects, so zones are not resizable.
/// </summary>
internal sealed class NzxtHue2Device : IRgbDevice, IFanController, ISensorProvider
{
    private readonly HidReportChannel _hid;
    private readonly int _fallbackDuty;
    private readonly bool _dryRun;
    private readonly Lock _transaction = new();
    private readonly int[] _zoneChannels;
    private readonly Dictionary<string, string> _properties;
    private bool _disposed;

    private NzxtHue2Device(
        HidReportChannel hid,
        Hue2Model model,
        string id,
        string firmware,
        IReadOnlyList<IReadOnlyList<byte>> accessories,
        int fanChannels,
        int fallbackDuty,
        bool dryRun)
    {
        _hid = hid;
        _fallbackDuty = fallbackDuty;
        _dryRun = dryRun;
        Id = id;

        var zones = new List<RgbZone>();
        var zoneChannels = new List<int>();
        for (var channel = 0; channel < accessories.Count; channel++)
        {
            var known = accessories[channel].Select(a => NzxtCatalog.Accessories.GetValueOrDefault(a)).OfType<Hue2Accessory>().ToList();
            if (known.Count == 0)
            {
                continue;
            }

            var segments = known.SelectMany(NzxtCatalog.Segments).ToList();
            var leds = LedLayouts.Row(segments).Take(NzxtProtocol.MaxLedsPerChannel).ToArray();
            zones.Add(new RgbZone
            {
                Id = $"led{channel + 1}",
                Name = $"Channel {channel + 1}: {string.Join(" + ", known.Select(a => a.Name))}",
                Topology = segments.Count == 1 ? segments[0].Topology : LedTopology.Composite,
                Leds = leds,
                MaxLedCount = NzxtProtocol.MaxLedsPerChannel,
            });
            zoneChannels.Add(channel);
        }

        Zones = zones;
        _zoneChannels = [.. zoneChannels];

        Channels = Enumerable.Range(0, fanChannels)
            .Select(i => new FanChannel($"fan{i + 1}", $"Fan {i + 1}", FanChannelKind.Fan, 0, 100, $"{id}/fan{i + 1}-rpm"))
            .ToArray();

        _properties = new Dictionary<string, string>
        {
            ["Firmware"] = firmware,
            ["ProductId"] = $"0x{model.ProductId:X4}",
            ["UsbPath"] = hid.Device.DevicePath,
            ["Accessories"] = string.Join("; ", accessories.Select((a, i) => $"ch{i + 1}: {(a.Count == 0 ? "none" : string.Join(", ", a.Select(x => NzxtCatalog.Accessories.TryGetValue(x, out var acc) ? acc.Name : $"0x{x:X2}")))}")),
        };
    }

    public string Id { get; }

    public string Name { get; private init; } = string.Empty;

    public string Vendor => "NZXT";

    public DeviceCapabilities Capabilities =>
        (Zones.Count > 0 ? DeviceCapabilities.Rgb : DeviceCapabilities.None)
        | (Channels.Count > 0 ? DeviceCapabilities.FanControl | DeviceCapabilities.Sensors : DeviceCapabilities.None);

    public IReadOnlyDictionary<string, string> Properties => _properties;

    public IReadOnlyList<RgbZone> Zones { get; }

    public int MaxFramesPerSecond => 30;

    public IReadOnlyList<FanChannel> Channels { get; }

    public static NzxtHue2Device Open(HidDevice hidDevice, Hue2Model model, string id, NzxtOptions options, bool dryRun)
    {
        var hid = HidReportChannel.Open(hidDevice);
        try
        {
            hid.DrainInput();
            var fanChannels = model.FanChannels;
            if (fanChannels > 0)
            {
                hid.Write(NzxtProtocol.SmartDeviceSetStatusInterval());
                hid.Write(NzxtProtocol.SmartDeviceFinishInitialization());
                var status = hid.ReadUntil(r => NzxtProtocol.IsResponse(r, 0x67, 0x02), 1500);
                fanChannels = status is null ? fanChannels : NzxtProtocol.FanChannelCount(status, fanChannels);
            }

            var firmwareReport = hid.Request(NzxtProtocol.FirmwareRequest(), r => NzxtProtocol.IsResponse(r, 0x11, 0x01), 1000);
            var firmware = firmwareReport is null ? "unknown" : NzxtProtocol.ParseFirmware(firmwareReport);

            IReadOnlyList<IReadOnlyList<byte>> accessories = [];
            if (model.LedChannels > 0)
            {
                var info = hid.Request(NzxtProtocol.LightingInfoRequest(), r => NzxtProtocol.IsResponse(r, 0x21, 0x03), 1000);
                accessories = info is null ? [] : NzxtProtocol.ParseAccessories(info, model.LedChannels);
            }

            var product = hidDevice.SafeProductName();
            return new NzxtHue2Device(hid, model, id, firmware, accessories, fanChannels, options.FallbackFanDutyPercent, dryRun)
            {
                Name = product.Length > 0 ? product : model.Name,
            };
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
        if (_dryRun)
        {
            return ValueTask.CompletedTask;
        }

        lock (_transaction)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var offset = 0;
            for (var i = 0; i < Zones.Count && offset < colors.Length; i++)
            {
                var count = Math.Min(Zones[i].LedCount, colors.Length - offset);
                foreach (var packet in NzxtProtocol.Hue2Frame(_zoneChannels[i], colors.Span.Slice(offset, count)))
                {
                    _hid.Write(packet);
                }

                offset += Zones[i].LedCount;
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<SensorReading>> ReadSensorsAsync(CancellationToken cancellationToken)
    {
        if (Channels.Count == 0)
        {
            return ValueTask.FromResult<IReadOnlyList<SensorReading>>([]);
        }

        IReadOnlyList<SmartDeviceFanStatus> fans;
        lock (_transaction)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // The controller pushes [0x67, 0x02] every 0.5 s; skip the backlog and wait for a fresh one.
            _hid.DrainInput();
            var report = _hid.ReadUntil(r => NzxtProtocol.IsResponse(r, 0x67, 0x02), 1500);
            fans = report is null ? [] : NzxtProtocol.ParseSmartDeviceFans(report, Channels.Count);
        }

        if (fans.Count == 0)
        {
            throw new IOException($"{Name} did not send a fan status report.");
        }

        var readings = new List<SensorReading>(fans.Count * 2);
        foreach (var fan in fans.Where(f => f.Connected))
        {
            readings.Add(new SensorReading($"{Id}/fan{fan.Channel + 1}-rpm", $"Fan {fan.Channel + 1} speed", SensorKind.FanSpeed, fan.Rpm));
            readings.Add(new SensorReading($"{Id}/fan{fan.Channel + 1}-duty", $"Fan {fan.Channel + 1} duty", SensorKind.Duty, fan.Duty));
        }

        return ValueTask.FromResult<IReadOnlyList<SensorReading>>(readings);
    }

    public ValueTask SetDutyAsync(string channelId, int dutyPercent, CancellationToken cancellationToken)
    {
        WriteDuty(ParseChannel(channelId), dutyPercent);
        return ValueTask.CompletedTask;
    }

    /// <summary>These controllers have no temperature input, so "default" is a fixed, safe duty.</summary>
    public ValueTask RestoreDefaultAsync(string channelId, CancellationToken cancellationToken)
    {
        WriteDuty(ParseChannel(channelId), _fallbackDuty);
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

    private void WriteDuty(int channel, int duty)
    {
        if (_dryRun)
        {
            return;
        }

        lock (_transaction)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _hid.Write(NzxtProtocol.SmartDeviceFixedDuty(channel, duty));
        }
    }

    private int ParseChannel(string channelId)
    {
        for (var i = 0; i < Channels.Count; i++)
        {
            if (Channels[i].Id == channelId)
            {
                return i;
            }
        }

        throw new ArgumentException($"Unknown channel '{channelId}'.", nameof(channelId));
    }
}
