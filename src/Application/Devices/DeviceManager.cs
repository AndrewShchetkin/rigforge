using Microsoft.Extensions.Logging;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Application.Devices;

/// <summary>
/// Owns every detected device and exposes capability-filtered views.
/// Engines read <see cref="Devices"/> once per iteration; a rescan swaps the whole list atomically.
/// </summary>
public sealed partial class DeviceManager(IEnumerable<IDeviceProbe> probes, ILogger<DeviceManager> logger) : IAsyncDisposable
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);

    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private volatile IReadOnlyList<IHardwareDevice> _devices = [];

    public event Action? DevicesChanged;

    public IReadOnlyList<IHardwareDevice> Devices => _devices;

    // A driver may implement an interface its particular hardware lacks (e.g. a Kraken whose screen
    // endpoint is busy), so the capability flags decide which views a device appears in.
    public IEnumerable<IRgbDevice> RgbDevices => With<IRgbDevice>(DeviceCapabilities.Rgb);

    public IEnumerable<IFanController> FanControllers => With<IFanController>(DeviceCapabilities.FanControl);

    public IEnumerable<ISensorProvider> SensorProviders => With<ISensorProvider>(DeviceCapabilities.Sensors);

    public IEnumerable<IAioLcdDevice> LcdDevices => With<IAioLcdDevice>(DeviceCapabilities.Lcd);

    public IHardwareDevice? Find(string deviceId) => _devices.FirstOrDefault(d => d.Id == deviceId);

    /// <summary>Releases all current devices and runs every probe again (in parallel).</summary>
    public async Task ScanAsync(CancellationToken cancellationToken)
    {
        await _scanLock.WaitAsync(cancellationToken);
        try
        {
            // Devices hold exclusive handles (WinUSB, hidraw), so they must be released before probing again.
            var previous = _devices;
            _devices = [];
            await DisposeAllAsync(previous);

            var results = await Task.WhenAll(probes.Select(probe => RunProbeAsync(probe, cancellationToken)));

            var unique = new List<IHardwareDevice>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var device in results.SelectMany(r => r))
            {
                if (seen.Add(device.Id))
                {
                    unique.Add(device);
                }
                else
                {
                    LogDuplicateDevice(device.Id);
                    await device.DisposeAsync();
                }
            }

            _devices = unique;
            LogScanCompleted(unique.Count);
        }
        finally
        {
            _scanLock.Release();
        }

        DevicesChanged?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        await _scanLock.WaitAsync();
        try
        {
            var devices = _devices;
            _devices = [];
            await DisposeAllAsync(devices);
        }
        finally
        {
            _scanLock.Release();
        }
    }

    private IEnumerable<T> With<T>(DeviceCapabilities capability) =>
        _devices.OfType<T>().Where(d => ((IHardwareDevice)d!).Capabilities.HasFlag(capability));

    private async Task<IReadOnlyList<IHardwareDevice>> RunProbeAsync(IDeviceProbe probe, CancellationToken cancellationToken)
    {
        try
        {
            // Probes may block on synchronous HID I/O, so run them off the caller's thread.
            var devices = await Task.Run(() => probe.ProbeAsync(cancellationToken), cancellationToken)
                .WaitAsync(ProbeTimeout, cancellationToken);

            foreach (var device in devices)
            {
                LogDeviceDetected(device.Name, device.Id, probe.Name, device.Capabilities.ToString());
            }

            return devices;
        }
        catch (TimeoutException)
        {
            LogProbeTimedOut(probe.Name, ProbeTimeout.TotalSeconds);
            return [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogProbeFailed(ex, probe.Name);
            return [];
        }
    }

    private async Task DisposeAllAsync(IReadOnlyList<IHardwareDevice> devices)
    {
        foreach (var device in devices)
        {
            try
            {
                await device.DisposeAsync();
            }
            catch (Exception ex)
            {
                LogDisposeFailed(ex, device.Id);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Detected {Name} ({Id}) via {Probe}: {Capabilities}")]
    private partial void LogDeviceDetected(string name, string id, string probe, string capabilities);

    [LoggerMessage(Level = LogLevel.Information, Message = "Device scan completed: {Count} device(s)")]
    private partial void LogScanCompleted(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Probe {Probe} did not finish within {Seconds} s and was skipped")]
    private partial void LogProbeTimedOut(string probe, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Probe {Probe} failed")]
    private partial void LogProbeFailed(Exception exception, string probe);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Device id {Id} reported twice; ignoring the duplicate")]
    private partial void LogDuplicateDevice(string id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to release device {Id}")]
    private partial void LogDisposeFailed(Exception exception, string id);
}
