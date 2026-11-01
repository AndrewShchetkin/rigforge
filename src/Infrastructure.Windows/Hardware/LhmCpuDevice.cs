using System.Runtime.Versioning;
using LibreHardwareMonitor.Hardware;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Infrastructure.Windows.Hardware;

/// <summary>CPU temperatures (AMD Tctl/Tdie, CCD), package power and total load via LibreHardwareMonitor.</summary>
[SupportedOSPlatform("windows")]
internal sealed class LhmCpuDevice : ISensorProvider
{
    // Preferred "CPU temperature" sensors, best first: AMD Zen, Intel, generic fallbacks.
    private static readonly string[] PackageTemperatureNames = ["Core (Tctl/Tdie)", "CPU Package", "Tctl", "Tdie", "Core (Tctl)"];

    private readonly LibreHardwareMonitorSession _session;
    private readonly IHardware _hardware;
    private int _released;

    public LhmCpuDevice(LibreHardwareMonitorSession session, IHardware hardware)
    {
        _session = session;
        _hardware = hardware;
        Id = LibreHardwareMonitorSession.DeviceId(hardware);
        Properties = new Dictionary<string, string>
        {
            ["Source"] = "LibreHardwareMonitor",
            ["Identifier"] = hardware.Identifier.ToString(),
        };

        lock (session.Sync)
        {
            foreach (var sensor in hardware.Sensors)
            {
                sensor.ValuesTimeWindow = TimeSpan.Zero;
            }
        }
    }

    public string Id { get; }

    public string Name => _hardware.Name;

    public string Vendor => _hardware.Name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ? "AMD" : "Intel";

    public DeviceCapabilities Capabilities => DeviceCapabilities.Sensors;

    public IReadOnlyDictionary<string, string> Properties { get; }

    public ValueTask<IReadOnlyList<SensorReading>> ReadSensorsAsync(CancellationToken cancellationToken)
    {
        var readings = new List<SensorReading>();
        lock (_session.Sync)
        {
            ObjectDisposedException.ThrowIf(_released != 0, this);
            _hardware.Update();

            var packageSensor = PackageTemperatureNames
                .Select(name => _hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name == name && s.Value > 0f))
                .FirstOrDefault(s => s is not null);

            foreach (var sensor in _hardware.Sensors)
            {
                sensor.ValuesTimeWindow = TimeSpan.Zero;

                if (sensor.Value is not { } value || !Include(sensor) || LibreHardwareMonitorSession.MapKind(sensor.SensorType) is not { } kind)
                {
                    continue;
                }

                // Without ring-0 access (no admin rights / PawnIO) LHM reports 0 °C; a fake cold CPU would
                // starve the fan curves, so drop it and let the cooling failsafe kick in instead.
                if (kind == SensorKind.Temperature && value is <= 0f or >= 150f)
                {
                    continue;
                }

                // Same cause: SMU power readings are all 0 W without ring-0 access.
                if (kind == SensorKind.Power && value <= 0f && packageSensor is null)
                {
                    continue;
                }

                var role = ReferenceEquals(sensor, packageSensor) ? SensorRole.CpuTemperature
                    : sensor.SensorType == SensorType.Load && sensor.Name == "CPU Total" ? SensorRole.CpuLoad
                    : SensorRole.None;

                readings.Add(new SensorReading($"{Id}/{LibreHardwareMonitorSession.SensorKey(sensor)}", sensor.Name, kind, value) { Role = role });
            }
        }

        return ValueTask.FromResult<IReadOnlyList<SensorReading>>(readings);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            _session.Release();
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Skips per-core clocks/loads to keep the telemetry stream small.</summary>
    private static bool Include(ISensor sensor) => sensor.SensorType switch
    {
        SensorType.Temperature or SensorType.Power => true,
        SensorType.Load => sensor.Name is "CPU Total" or "CPU Core Max",
        _ => false,
    };
}
