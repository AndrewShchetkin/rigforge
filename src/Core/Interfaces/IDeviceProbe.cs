namespace RigForge.Core.Interfaces;

/// <summary>Detects and initializes one family of devices (NZXT HID, ASUS Aura, NVML, hwmon...).</summary>
public interface IDeviceProbe
{
    string Name { get; }

    /// <summary>Returns the initialized devices; must return an empty list (not throw) when no hardware is present.</summary>
    Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Incremental probe: receives the devices this probe returned on the previous scan so already-open
    /// handles (e.g. LibreHardwareMonitor, NVML, SMBus, or unchanged USB devices) can be kept alive.
    /// </summary>
    Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(IReadOnlyList<IHardwareDevice> existingDevices, CancellationToken cancellationToken) =>
        ProbeAsync(cancellationToken);
}
