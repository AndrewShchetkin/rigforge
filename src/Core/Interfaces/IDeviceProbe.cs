namespace RigForge.Core.Interfaces;

/// <summary>Detects and initializes one family of devices (NZXT HID, ASUS Aura, NVML, hwmon...).</summary>
public interface IDeviceProbe
{
    string Name { get; }

    /// <summary>Returns the initialized devices; must return an empty list (not throw) when no hardware is present.</summary>
    Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken);
}
