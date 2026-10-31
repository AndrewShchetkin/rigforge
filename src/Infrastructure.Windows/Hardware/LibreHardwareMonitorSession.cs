using System.Runtime.Versioning;
using LibreHardwareMonitor.Hardware;
using RigForge.Core.Models;

namespace RigForge.Infrastructure.Windows.Hardware;

/// <summary>
/// Shared LibreHardwareMonitor <see cref="Computer"/>. LHM is not thread-safe, so every call goes through
/// <see cref="Sync"/>; the computer is closed (restoring BIOS fan control) when the last device releases it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class LibreHardwareMonitorSession
{
    private readonly Computer _computer;
    private int _references = 1;

    public LibreHardwareMonitorSession(Computer computer) => _computer = computer;

    public Lock Sync { get; } = new();

    public IList<IHardware> Hardware => _computer.Hardware;

    public LibreHardwareMonitorSession AddReference()
    {
        Interlocked.Increment(ref _references);
        return this;
    }

    public void Release()
    {
        if (Interlocked.Decrement(ref _references) == 0)
        {
            lock (Sync)
            {
                _computer.Close();
            }
        }
    }

    public static string DeviceId(IHardware hardware) =>
        "lhm:" + hardware.Identifier.ToString().Trim('/').Replace('/', '-');

    /// <summary>"/amdcpu/0/temperature/2" → "temperature/2".</summary>
    public static string SensorKey(ISensor sensor)
    {
        var parts = sensor.Identifier.ToString().Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[^2]}/{parts[^1]}" : sensor.Name;
    }

    public static SensorKind? MapKind(SensorType type) => type switch
    {
        SensorType.Temperature => SensorKind.Temperature,
        SensorType.Load => SensorKind.Load,
        SensorType.Fan => SensorKind.FanSpeed,
        SensorType.Control => SensorKind.Duty,
        SensorType.Power => SensorKind.Power,
        SensorType.Clock => SensorKind.Clock,
        SensorType.Voltage => SensorKind.Voltage,
        _ => null,
    };
}
