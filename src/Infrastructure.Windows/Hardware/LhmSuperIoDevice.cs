using System.Runtime.Versioning;
using LibreHardwareMonitor.Hardware;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Infrastructure.Windows.Hardware;

/// <summary>
/// Motherboard Super I/O chip (e.g. Nuvoton NCT6799D on ASUS B850): temperatures, fan RPM and
/// software control of the PWM fan headers. Restoring a channel hands it back to the BIOS (Smart Fan).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class LhmSuperIoDevice : ISensorProvider, IFanController
{
    private readonly LibreHardwareMonitorSession _session;
    private readonly IHardware _hardware;
    private readonly bool _dryRun;
    private readonly Dictionary<string, ISensor> _controls;
    private int _released;

    public LhmSuperIoDevice(LibreHardwareMonitorSession session, IHardware hardware, string motherboardName, bool allowFanControl, bool dryRun)
    {
        _session = session;
        _hardware = hardware;
        _dryRun = dryRun;
        Id = LibreHardwareMonitorSession.DeviceId(hardware);

        lock (session.Sync)
        {
            hardware.Update();
            _controls = allowFanControl
                ? hardware.Sensors.Where(s => s.SensorType == SensorType.Control && s.Control is not null)
                    .ToDictionary(s => LibreHardwareMonitorSession.SensorKey(s), StringComparer.Ordinal)
                : [];

            Channels = _controls.Select(pair =>
            {
                var index = pair.Key.Split('/')[^1];
                var fan = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Fan && LibreHardwareMonitorSession.SensorKey(s) == $"fan/{index}");
                return new FanChannel(pair.Key, pair.Value.Name, FanChannelKind.Fan, 0, 100, fan is null ? null : $"{Id}/fan/{index}");
            }).ToArray();
        }

        Properties = new Dictionary<string, string>
        {
            ["Source"] = "LibreHardwareMonitor",
            ["Chip"] = hardware.Name,
            ["Motherboard"] = motherboardName,
            ["Identifier"] = hardware.Identifier.ToString(),
        };
    }

    public string Id { get; }

    public string Name => $"{_hardware.Name} (Super I/O)";

    public string Vendor => "Motherboard";

    public DeviceCapabilities Capabilities =>
        DeviceCapabilities.Sensors | (Channels.Count > 0 ? DeviceCapabilities.FanControl : DeviceCapabilities.None);

    public IReadOnlyDictionary<string, string> Properties { get; }

    public IReadOnlyList<FanChannel> Channels { get; }

    public ValueTask<IReadOnlyList<SensorReading>> ReadSensorsAsync(CancellationToken cancellationToken)
    {
        var readings = new List<SensorReading>();
        lock (_session.Sync)
        {
            ObjectDisposedException.ThrowIf(_released != 0, this);
            _hardware.Update();
            foreach (var sensor in _hardware.Sensors)
            {
                if (sensor.Value is { } value
                    && sensor.SensorType is SensorType.Temperature or SensorType.Fan or SensorType.Control
                    && LibreHardwareMonitorSession.MapKind(sensor.SensorType) is { } kind
                    && !(sensor.SensorType == SensorType.Temperature && (value <= 0f || value >= 127f)))
                {
                    readings.Add(new SensorReading($"{Id}/{LibreHardwareMonitorSession.SensorKey(sensor)}", sensor.Name, kind, value));
                }
            }
        }

        return ValueTask.FromResult<IReadOnlyList<SensorReading>>(readings);
    }

    public ValueTask SetDutyAsync(string channelId, int dutyPercent, CancellationToken cancellationToken)
    {
        WithControl(channelId, control => control.SetSoftware(Math.Clamp(dutyPercent, 0, 100)));
        return ValueTask.CompletedTask;
    }

    public ValueTask RestoreDefaultAsync(string channelId, CancellationToken cancellationToken)
    {
        WithControl(channelId, control => control.SetDefault());
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            _session.Release();
        }

        return ValueTask.CompletedTask;
    }

    private void WithControl(string channelId, Action<IControl> action)
    {
        if (_dryRun)
        {
            return;
        }

        if (!_controls.TryGetValue(channelId, out var sensor) || sensor.Control is not { } control)
        {
            throw new ArgumentException($"Unknown channel '{channelId}'.", nameof(channelId));
        }

        lock (_session.Sync)
        {
            ObjectDisposedException.ThrowIf(_released != 0, this);
            action(control);
        }
    }
}
