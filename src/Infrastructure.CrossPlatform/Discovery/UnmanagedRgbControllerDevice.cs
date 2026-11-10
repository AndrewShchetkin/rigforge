using System.Globalization;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.Discovery;

/// <summary>
/// A lighting controller that is visible on the bus but has no direct RGB driver yet.
/// It stays in the device list with a name and a path, and it accepts no commands.
/// </summary>
internal sealed class UnmanagedRgbControllerDevice : IHardwareDevice
{
    public UnmanagedRgbControllerDevice(
        int vendorId,
        int productId,
        string name,
        string vendor,
        string path,
        string transport,
        string? usagePage,
        string? serial,
        string? statusReason = null)
    {
        Id = $"unmanaged:{vendorId:x4}:{productId:x4}:{(string.IsNullOrWhiteSpace(serial) ? HashPath(path) : serial.Trim())}";
        Name = name.Length > 0 ? name : $"{vendor} {vendorId:X4}:{productId:X4}";
        Vendor = vendor.Length > 0 ? vendor : "Unknown";
        StatusReason = statusReason ?? $"Detected on {transport} ({vendorId:X4}:{productId:X4}), direct RGB protocol not yet implemented";
        var properties = new Dictionary<string, string>
        {
            ["vidPid"] = $"{vendorId:X4}:{productId:X4}",
            ["transport"] = transport,
            ["path"] = path,
        };
        if (!string.IsNullOrWhiteSpace(usagePage))
        {
            properties["usagePage"] = usagePage;
        }

        Properties = properties;
    }

    public string Id { get; }

    public string Name { get; }

    public string Vendor { get; }

    public DeviceCapabilities Capabilities => DeviceCapabilities.None;

    public DeviceSupportStatus SupportStatus => DeviceSupportStatus.Unmanaged;

    public string? StatusReason { get; }

    public IReadOnlyDictionary<string, string> Properties { get; }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>NVIDIA GPUs stay on NVML. Partner lighting needs its own USB or I2C driver.</summary>
    public static UnmanagedRgbControllerDevice ForNvidiaPartner(string gpuName, string path) =>
        new(
            vendorId: 0x10DE,
            productId: 0,
            name: gpuName.Length > 0 ? gpuName : "NVIDIA GPU",
            vendor: "NVIDIA",
            path: path,
            transport: "NVML",
            usagePage: null,
            serial: null,
            statusReason: "NVIDIA GPU detected via NVML; partner RGB controller requires dedicated USB/I2C driver");

    private static string HashPath(string path)
    {
        var hash = 2166136261u;
        foreach (var ch in path)
        {
            hash ^= ch;
            hash *= 16777619u;
        }

        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }
}
