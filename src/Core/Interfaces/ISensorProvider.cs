using RigForge.Core.Models;

namespace RigForge.Core.Interfaces;

/// <summary>Source of telemetry (CPU/GPU temperatures, liquid temperature, RPM, load...).</summary>
public interface ISensorProvider : IHardwareDevice
{
    /// <summary>Reads the current values. Sensor ids must be stable between calls.</summary>
    ValueTask<IReadOnlyList<SensorReading>> ReadSensorsAsync(CancellationToken cancellationToken);
}
