namespace RigForge.Core.Models;

/// <summary>How much of a detected device RigForge can drive.</summary>
public enum DeviceSupportStatus
{
    /// <summary>Lighting, cooling, or a screen is under RigForge control.</summary>
    Supported,

    /// <summary>Sensors are read, but the device is not otherwise controlled.</summary>
    TelemetryOnly,

    /// <summary>The device was detected and is left to its own firmware or another program.</summary>
    Unmanaged,
}
