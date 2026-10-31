using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using RigForge.Application.Cooling;
using RigForge.Application.Devices;
using RigForge.Application.Effects;
using RigForge.Application.Layout;
using RigForge.Application.Profiles;
using RigForge.Application.Telemetry;
using RigForge.Host.Daemon.Contracts;
using RigForge.Infrastructure.CrossPlatform.Diagnostics;

namespace RigForge.Host.Daemon.Services;

/// <summary>Assembles the full <see cref="SystemStateDto"/> sent to UI clients.</summary>
public sealed class SystemStateBuilder(
    DeviceManager devices,
    ProfileService profiles,
    TelemetryService telemetry,
    FanControlEngine fans,
    EffectRegistry effects,
    CanvasLayoutService layout)
{
    private static readonly TimeSpan WarningsCacheDuration = TimeSpan.FromSeconds(10);
    private static readonly string Version =
        typeof(SystemStateBuilder).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    private readonly Lock _warningsGate = new();
    private IReadOnlyList<string> _warnings = [];
    private long? _warningsCheckedAt;

    public SystemStateDto Build() => new(
        BuildInfo(),
        devices.Devices.Select(DtoMapper.ToDto).ToArray(),
        profiles.Current,
        layout.GetEffectiveCanvas(),
        effects.Descriptors.Select(DtoMapper.ToDto).ToArray(),
        DtoMapper.ToDto(telemetry.Latest),
        fans.Status.Select(DtoMapper.ToDto).ToArray(),
        Warnings());

    public DaemonInfoDto BuildInfo()
    {
        using var process = Process.GetCurrentProcess();
        return new DaemonInfoDto(
            Version,
            $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}",
            process.StartTime.ToUniversalTime(),
            Environment.WorkingSet,
            GC.GetTotalMemory(forceFullCollection: false));
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
