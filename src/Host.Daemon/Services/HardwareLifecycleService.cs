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
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        profiles.Changed -= OnProfileChanged;
        devices.DevicesChanged -= layout.ApplyZoneSizes;
        await devices.DisposeAsync();
    }

    private void OnProfileChanged(RigProfile profile) => layout.ApplyZoneSizes();

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Warning}")]
    private partial void LogConflict(string warning);
}
