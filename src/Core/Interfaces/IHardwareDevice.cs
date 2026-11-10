using RigForge.Core.Models;

namespace RigForge.Core.Interfaces;

/// <summary>
/// Common identity of every detected device. A physical device implements one or more capability
/// interfaces (<see cref="IRgbDevice"/>, <see cref="IFanController"/>, <see cref="ISensorProvider"/>, <see cref="IAioLcdDevice"/>)
/// over a single shared connection.
/// </summary>
public interface IHardwareDevice : IAsyncDisposable
{
    /// <summary>Identifier that is stable across reboots (vendor, model and serial number based).</summary>
    string Id { get; }

    string Name { get; }

    string Vendor { get; }

    DeviceCapabilities Capabilities { get; }

    /// <summary>
    /// Derived from <see cref="Capabilities"/> unless an implementation overrides it.
    /// Sensor-only devices stay telemetry; devices with no capabilities are left unmanaged.
    /// </summary>
    DeviceSupportStatus SupportStatus =>
        Capabilities == DeviceCapabilities.None ? DeviceSupportStatus.Unmanaged :
        Capabilities == DeviceCapabilities.Sensors ? DeviceSupportStatus.TelemetryOnly :
        DeviceSupportStatus.Supported;

    /// <summary>Why <see cref="SupportStatus"/> is not <see cref="DeviceSupportStatus.Supported"/>, when there is a reason worth showing.</summary>
    string? StatusReason => null;

    /// <summary>Diagnostic details such as firmware version or USB path.</summary>
    IReadOnlyDictionary<string, string> Properties { get; }
}
