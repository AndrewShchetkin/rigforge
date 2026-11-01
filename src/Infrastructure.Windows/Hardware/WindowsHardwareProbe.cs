using System.Runtime.Versioning;
using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;
using RigForge.Infrastructure.CrossPlatform;

namespace RigForge.Infrastructure.Windows.Hardware;

/// <summary>Windows system sensors (always) plus CPU and Super I/O through LibreHardwareMonitor (optional).</summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsHardwareProbe(
    IOptions<WindowsPlatformOptions> options,
    IOptions<HardwareOptions> hardwareOptions,
    ILogger<WindowsHardwareProbe> logger) : IDeviceProbe
{
    public string Name => "Windows sensors (Win32 + LibreHardwareMonitor)";

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(
        IReadOnlyList<IHardwareDevice> existingDevices,
        CancellationToken cancellationToken) =>
        existingDevices.Count > 0
            ? Task.FromResult(existingDevices)
            : ProbeAsync(cancellationToken);

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken)
    {
        var devices = new List<IHardwareDevice> { new WindowsSystemDevice() };
        var settings = options.Value;
        if (!settings.EnableLibreHardwareMonitor)
        {
            return Task.FromResult<IReadOnlyList<IHardwareDevice>>(devices);
        }

        if (!Environment.IsPrivilegedProcess)
        {
            LogNotElevated();
        }

        var computer = new Computer { IsCpuEnabled = true, IsMotherboardEnabled = true };
        try
        {
            computer.Open();
        }
        catch (Exception ex)
        {
            LogOpenFailed(ex);
            return Task.FromResult<IReadOnlyList<IHardwareDevice>>(devices);
        }

        var session = new LibreHardwareMonitorSession(computer);
        lock (session.Sync)
        {
            foreach (var hardware in session.Hardware)
            {
                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    devices.Add(new LhmCpuDevice(session.AddReference(), hardware));
                }
                else if (hardware.HardwareType == HardwareType.Motherboard)
                {
                    foreach (var superIo in hardware.SubHardware.Where(h => h.HardwareType == HardwareType.SuperIO))
                    {
                        devices.Add(new LhmSuperIoDevice(session.AddReference(), superIo, hardware.Name, settings.EnableMotherboardFanControl, hardwareOptions.Value.DryRun));
                    }
                }
            }
        }

        if (devices.Count == 1)
        {
            LogNothingFound();
        }

        // Devices hold their own references; drop the probe's.
        session.Release();
        return Task.FromResult<IReadOnlyList<IHardwareDevice>>(devices);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Not running as administrator: LibreHardwareMonitor cannot read CPU temperatures or the Super I/O chip")]
    private partial void LogNotElevated();

    [LoggerMessage(Level = LogLevel.Warning, Message = "LibreHardwareMonitor failed to start")]
    private partial void LogOpenFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "LibreHardwareMonitor found no CPU or Super I/O sensors; install the PawnIO driver (https://pawnio.eu) and run elevated")]
    private partial void LogNothingFound();
}
