using System.Globalization;
using System.Runtime;
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
    private readonly Dictionary<IDeviceProbe, IReadOnlyList<IHardwareDevice>> _byProbe = [];
    private volatile DeviceSnapshot _snapshot = DeviceSnapshot.Empty;

    public event Action? DevicesChanged;

    public IReadOnlyList<IHardwareDevice> Devices => _snapshot.All;

    // A driver may implement an interface its particular hardware lacks (e.g. a Kraken whose screen
    // endpoint is busy), so the capability flags decide which views a device appears in.
    public IReadOnlyList<IRgbDevice> RgbDevices => _snapshot.RgbDevices;

    public IReadOnlyList<IFanController> FanControllers => _snapshot.FanControllers;

    public IReadOnlyList<ISensorProvider> SensorProviders => _snapshot.SensorProviders;

    public IReadOnlyList<IAioLcdDevice> LcdDevices => _snapshot.LcdDevices;

    public IHardwareDevice? Find(string deviceId) => _snapshot.All.FirstOrDefault(d => d.Id == deviceId);

    /// <summary>Runs every probe in parallel, keeping unchanged devices alive and disposing only removed ones.</summary>
    public async Task ScanAsync(CancellationToken cancellationToken)
    {
        await _scanLock.WaitAsync(cancellationToken);
        try
        {
            var previous = _snapshot.All;
            var previousSet = new HashSet<IHardwareDevice>(previous, ReferenceEqualityComparer.Instance);

            var results = await Task.WhenAll(
                probes.Select(probe => RunProbeAsync(probe, _byProbe.GetValueOrDefault(probe, []), cancellationToken)));

            var unique = new List<IHardwareDevice>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var addedCount = 0;

            foreach (var (probe, probeDevices) in results)
            {
                var acceptedForProbe = new List<IHardwareDevice>(probeDevices.Count);
                foreach (var device in probeDevices)
                {
                    if (seen.Add(device.Id))
                    {
                        unique.Add(device);
                        acceptedForProbe.Add(device);
                        if (!previousSet.Contains(device))
                        {
                            addedCount++;
                            LogDeviceDetected(device.Name, device.Id, probe.Name, device.Capabilities.ToString());
                        }
                    }
                    else
                    {
                        LogDuplicateDevice(device.Id);
                        if (!previousSet.Contains(device))
                        {
                            await DisposeAllAsync([device]);
                        }
                    }
                }

                _byProbe[probe] = acceptedForProbe;
            }

            await DropUnmanagedDuplicatesAsync(unique, previousSet);
            var retainedRefs = new HashSet<IHardwareDevice>(unique, ReferenceEqualityComparer.Instance);
            foreach (var probe in _byProbe.Keys.ToArray())
            {
                _byProbe[probe] = _byProbe[probe].Where(device => retainedRefs.Contains(device)).ToArray();
            }

            var retainedSet = retainedRefs;
            var removed = previous.Where(d => !retainedSet.Contains(d)).ToArray();
            if (removed.Length > 0)
            {
                await DisposeAllAsync(removed);
            }

            _snapshot = DeviceSnapshot.Create(unique);
            LogScanCompleted(unique.Count);

            if (addedCount > 0 || removed.Length > 0)
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            }
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
            var devices = _snapshot.All;
            _snapshot = DeviceSnapshot.Empty;
            _byProbe.Clear();
            await DisposeAllAsync(devices);
        }
        finally
        {
            _scanLock.Release();
        }
    }

    private sealed record DeviceSnapshot(
        IReadOnlyList<IHardwareDevice> All,
        IReadOnlyList<IRgbDevice> RgbDevices,
        IReadOnlyList<IFanController> FanControllers,
        IReadOnlyList<ISensorProvider> SensorProviders,
        IReadOnlyList<IAioLcdDevice> LcdDevices)
    {
        public static DeviceSnapshot Empty { get; } = new([], [], [], [], []);

        public static DeviceSnapshot Create(IReadOnlyList<IHardwareDevice> devices) => new(
            devices,
            With<IRgbDevice>(devices, DeviceCapabilities.Rgb),
            With<IFanController>(devices, DeviceCapabilities.FanControl),
            With<ISensorProvider>(devices, DeviceCapabilities.Sensors),
            With<IAioLcdDevice>(devices, DeviceCapabilities.Lcd));

        private static IReadOnlyList<T> With<T>(IReadOnlyList<IHardwareDevice> devices, DeviceCapabilities capability) =>
            devices.Where(d => d.Capabilities.HasFlag(capability)).OfType<T>().ToArray();
    }

    private async Task<(IDeviceProbe Probe, IReadOnlyList<IHardwareDevice> Devices)> RunProbeAsync(
        IDeviceProbe probe,
        IReadOnlyList<IHardwareDevice> existingForProbe,
        CancellationToken cancellationToken)
    {
        try
        {
            // Probes may block on synchronous HID I/O, so run them off the caller's thread.
            var devices = await Task.Run(() => probe.ProbeAsync(existingForProbe, cancellationToken), cancellationToken)
                .WaitAsync(ProbeTimeout, cancellationToken);

            return (probe, devices);
        }
        catch (TimeoutException)
        {
            LogProbeTimedOut(probe.Name, ProbeTimeout.TotalSeconds);
            return (probe, []);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogProbeFailed(ex, probe.Name);
            return (probe, []);
        }
    }

    private async Task DropUnmanagedDuplicatesAsync(List<IHardwareDevice> unique, HashSet<IHardwareDevice> previousSet)
    {
        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in unique)
        {
            if (device.SupportStatus == DeviceSupportStatus.Supported && TryReadVidPid(device, out var vidPid))
            {
                supported.Add(vidPid);
            }
        }

        if (supported.Count == 0)
        {
            return;
        }

        var created = new List<IHardwareDevice>();
        unique.RemoveAll(device =>
        {
            if (device.SupportStatus != DeviceSupportStatus.Unmanaged
                || !TryReadVidPid(device, out var vidPid)
                || !supported.Contains(vidPid))
            {
                return false;
            }

            LogUnmanagedDuplicate(device.Id, vidPid);
            if (!previousSet.Contains(device))
            {
                created.Add(device);
            }

            return true;
        });

        if (created.Count > 0)
        {
            await DisposeAllAsync(created);
        }
    }

    private static bool TryReadVidPid(IHardwareDevice device, out string vidPid)
    {
        vidPid = string.Empty;
        if (device.Properties.TryGetValue("vidPid", out var raw) && TryNormalizeVidPid(raw, out vidPid))
        {
            return true;
        }

        return device.Properties.TryGetValue("VendorId", out var vendor)
            && device.Properties.TryGetValue("ProductId", out var product)
            && TryNormalizeVidPid($"{vendor}:{product}", out vidPid);
    }

    private static bool TryNormalizeVidPid(string raw, out string vidPid)
    {
        vidPid = string.Empty;
        var parts = raw.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !TryHex(parts[0], out var vendor) || !TryHex(parts[1], out var product))
        {
            return false;
        }

        vidPid = $"{vendor:X4}:{product:X4}";
        return true;
    }

    private static bool TryHex(string text, out int value)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Dropped unmanaged duplicate {Id} ({VidPid}); a supported device owns that USB id")]
    private partial void LogUnmanagedDuplicate(string id, string vidPid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to release device {Id}")]
    private partial void LogDisposeFailed(Exception exception, string id);
}
