using HidSharp;
using RigForge.Application.Devices;
using RigForge.Application.Layout;
using RigForge.Application.Profiles;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Diagnostics;

namespace RigForge.Host.Daemon.Services;

/// <summary>
/// Registered before the engine workers: loads the profile and detects devices before any loop starts,
/// and (being stopped last) releases the devices after every loop has handed its hardware back.
/// </summary>
internal sealed partial class HardwareLifecycleService(
    ProfileService profiles,
    DeviceManager devices,
    CanvasLayoutService layout,
    ILogger<HardwareLifecycleService> logger) : IHostedService
{
    private static readonly TimeSpan HotplugDebounce = TimeSpan.FromMilliseconds(750);

    private readonly Lock _hotplugGate = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private CancellationTokenSource? _debounceCts;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await profiles.InitializeAsync(cancellationToken);

        foreach (var warning in ConflictingSoftwareDetector.Detect())
        {
            LogConflict(warning);
        }

        await devices.ScanAsync(cancellationToken);
        layout.ApplyZoneSizes();

        profiles.Changed += OnProfileChanged;
        devices.DevicesChanged += layout.ApplyZoneSizes;
        DeviceList.Local.Changed += OnHidDeviceListChanged;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        DeviceList.Local.Changed -= OnHidDeviceListChanged;
        profiles.Changed -= OnProfileChanged;
        devices.DevicesChanged -= layout.ApplyZoneSizes;

        await _shutdownCts.CancelAsync();
        lock (_hotplugGate)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }

        await devices.DisposeAsync();
        _shutdownCts.Dispose();
    }

    private void OnProfileChanged(RigProfile profile) => layout.ApplyZoneSizes();

    private void OnHidDeviceListChanged(object? sender, DeviceListChangedEventArgs e)
    {
        CancellationToken token;
        lock (_hotplugGate)
        {
            if (_shutdownCts.IsCancellationRequested)
            {
                return;
            }

            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
            token = _debounceCts.Token;
        }

        _ = RescanAfterDelayAsync(token);
    }

    private async Task RescanAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(HotplugDebounce, cancellationToken);
            LogHotplugDetected();
            await devices.ScanAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LogHotplugScanFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Warning}")]
    private partial void LogConflict(string warning);

    [LoggerMessage(Level = LogLevel.Information, Message = "USB device list changed; running incremental device scan")]
    private partial void LogHotplugDetected();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Incremental device scan after USB hotplug failed")]
    private partial void LogHotplugScanFailed(Exception exception);
}
