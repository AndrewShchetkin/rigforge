using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using RigForge.Application.Cooling;
using RigForge.Application.Diagnostics;
using RigForge.Application.Devices;
using RigForge.Application.Effects;
using RigForge.Application.Layout;
using RigForge.Application.Profiles;
using RigForge.Application.Telemetry;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Host.Daemon.Contracts;
using RigForge.Host.Daemon.Hosting;
using RigForge.Infrastructure.CrossPlatform.Diagnostics;

namespace RigForge.Host.Daemon.Services;

/// <summary>Assembles the full <see cref="SystemStateDto"/> sent to UI clients.</summary>
public sealed class SystemStateBuilder(
    DeviceManager devices,
    ProfileService profiles,
    TelemetryService telemetry,
    FanControlEngine fans,
    EffectRegistry effects,
    CanvasLayoutService layout,
    PerformanceMonitor performance,
    IAutostartService autostart)
{
    private static readonly TimeSpan WarningsCacheDuration = TimeSpan.FromSeconds(10);
    private static readonly string Version =
        typeof(SystemStateBuilder).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
    private static readonly string Platform =
        $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}";
    private static readonly DateTimeOffset StartedAt = GetStartedAt();

    private readonly EffectDescriptorDto[] _effects = effects.Descriptors.Select(DtoMapper.ToDto).ToArray();
    private readonly Lock _devicesGate = new();
    private readonly Lock _warningsGate = new();
    private IReadOnlyList<IHardwareDevice>? _cachedDeviceSource;
    private IReadOnlyList<RgbZone>[]? _cachedDeviceZones;
    private DeviceDto[] _cachedDeviceDtos = [];
    private IReadOnlyList<string> _warnings = [];
    private long? _warningsCheckedAt;

    public SystemStateDto Build() => new(
        BuildInfo(),
        BuildDevices(),
        profiles.Current,
        layout.GetEffectiveCanvas(),
        _effects,
        DtoMapper.ToDto(telemetry.Latest),
        fans.Status.Select(DtoMapper.ToDto).ToArray(),
        Warnings(),
        DtoMapper.ToDto(performance.Capture()));

    public DaemonInfoDto BuildInfo()
    {
        using var process = Process.GetCurrentProcess();
        var status = autostart.GetStatus();
        return new DaemonInfoDto(
            Version,
            Platform,
            StartedAt,
            process.PrivateMemorySize64,
            process.WorkingSet64,
            GC.GetTotalMemory(forceFullCollection: false),
            Product: "RigForge",
            SessionId: process.SessionId,
            AutostartSupported: status.Supported,
            AutostartEnabled: status.Enabled);
    }

    private static DateTimeOffset GetStartedAt()
    {
        using var process = Process.GetCurrentProcess();
        return process.StartTime.ToUniversalTime();
    }

    private DeviceDto[] BuildDevices()
    {
        var current = devices.Devices;
        lock (_devicesGate)
        {
            if (ReferenceEquals(current, _cachedDeviceSource) && _cachedDeviceZones is { } cachedZones)
            {
                var zonesUnchanged = true;
                var rgbIndex = 0;
                for (var i = 0; i < current.Count; i++)
                {
                    if (current[i] is IRgbDevice rgb)
                    {
                        if (rgbIndex >= cachedZones.Length || !ReferenceEquals(rgb.Zones, cachedZones[rgbIndex]))
                        {
                            zonesUnchanged = false;
                            break;
                        }

                        rgbIndex++;
                    }
                }

                if (zonesUnchanged && rgbIndex == cachedZones.Length)
                {
                    return _cachedDeviceDtos;
                }
            }

            _cachedDeviceSource = current;
            _cachedDeviceZones = current.OfType<IRgbDevice>().Select(d => d.Zones).ToArray();
            _cachedDeviceDtos = current.Select(DtoMapper.ToDto).ToArray();
            return _cachedDeviceDtos;
        }
    }

    /// <summary>Conflicting vendor software, re-checked at most every 10 seconds.</summary>
    private IReadOnlyList<string> Warnings()
    {
        lock (_warningsGate)
        {
            var now = Environment.TickCount64;
            if (_warningsCheckedAt is not { } checkedAt || now - checkedAt >= (long)WarningsCacheDuration.TotalMilliseconds)
            {
                _warnings = ConflictingSoftwareDetector.Detect();
                _warningsCheckedAt = now;
            }

            return _warnings;
        }
    }
}
