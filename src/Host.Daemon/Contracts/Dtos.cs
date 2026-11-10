using System.Text.Json;
using RigForge.Application.Effects;
using RigForge.Core.Models;

namespace RigForge.Host.Daemon.Contracts;

// Wire contract of the SignalR hub (/hubs/control) and the REST API (/api/*).
// Mirrored by ui/src/api/contracts.ts — keep both in sync.
// Profile documents (RigProfile, VirtualCanvas, FanCurve, ...) travel as-is: they are plain settings records.

/// <summary>Daemon process information.</summary>
public sealed record DaemonInfoDto(
    string Version,
    string Platform,
    DateTimeOffset StartedAt,
    long PrivateBytes,
    long WorkingSetBytes,
    long ManagedHeapBytes);

/// <summary>Process and lighting-loop sample pushed with telemetry.</summary>
public sealed record PerformanceMetricsDto(
    DateTimeOffset Timestamp,
    double CpuPercent,
    long PrivateBytes,
    long WorkingSetBytes,
    long ManagedHeapBytes,
    double EffectFps,
    double FrameTimeMicroseconds,
    EffectRenderMode RenderMode,
    int ActiveRgbDevices,
    int TotalControlledLeds,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections);

/// <summary>A detected device and everything it exposes.</summary>
public sealed record DeviceDto(
    string Id,
    string Name,
    string Vendor,
    IReadOnlyList<string> Capabilities,
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyList<RgbZoneDto> Zones,
    IReadOnlyList<FanChannelDto> FanChannels,
    LcdInfoDto? Lcd,
    DeviceSupportStatus SupportStatus,
    string? StatusReason);

public sealed record RgbZoneDto(
    string Id,
    string Name,
    LedTopology Topology,
    int LedCount,
    bool IsResizable,
    int MaxLedCount,
    IReadOnlyList<LedPositionDto> Leds);

public sealed record LedPositionDto(float X, float Y);

public sealed record FanChannelDto(
    string Id,
    string Name,
    FanChannelKind Kind,
    int MinDutyPercent,
    int MaxDutyPercent,
    string? SpeedSensorId);

public sealed record LcdInfoDto(int Width, int Height, bool IsCircular, float MaxFramesPerSecond);

public sealed record SensorReadingDto(
    string Id,
    string Name,
    SensorKind Kind,
    SensorRole Role,
    float Value,
    string Unit,
    DateTimeOffset Timestamp);

public sealed record TelemetryDto(DateTimeOffset Timestamp, IReadOnlyList<SensorReadingDto> Readings);

/// <summary>Live state of one fan curve binding.</summary>
public sealed record FanStatusDto(
    string CurveId,
    string DeviceId,
    string ChannelId,
    float? Temperature,
    int TargetDuty,
    int AppliedDuty,
    bool Failsafe,
    string? Message);

/// <summary>One choice shown for a <see cref="EffectParameterKind.Select"/> parameter.</summary>
public sealed record EffectSelectOptionDto(string Value, string Label);

/// <summary>One typed effect parameter; the UI builds its editor control from this schema.</summary>
public sealed record EffectParameterDefinitionDto(
    string Key,
    string Label,
    EffectParameterKind Kind,
    JsonElement DefaultValue,
    double? Min,
    double? Max,
    double? Step,
    string? Unit,
    int MinColors,
    int MaxColors,
    IReadOnlyList<EffectSelectOptionDto>? Options = null);

public sealed record EffectDescriptorDto(
    string Id,
    string Name,
    string Description,
    bool IsAnimated,
    IReadOnlyList<EffectParameterDefinitionDto> Parameters,
    EffectCategory Category = EffectCategory.Flowing,
    bool UsesAudio = false);

/// <summary>Live LED colors of one zone: RGB triplets in LED order, serialized as base64.</summary>
public sealed record ZonePreviewDto(string DeviceId, string ZoneId, byte[] Colors);

public sealed record LedPreviewDto(IReadOnlyList<ZonePreviewDto> Zones);

/// <summary>Full snapshot sent on connect and whenever devices or the profile change.</summary>
public sealed record SystemStateDto(
    DaemonInfoDto Info,
    IReadOnlyList<DeviceDto> Devices,
    RigProfile Profile,
    VirtualCanvas EffectiveCanvas,
    IReadOnlyList<EffectDescriptorDto> Effects,
    TelemetryDto Telemetry,
    IReadOnlyList<FanStatusDto> FanStatus,
    IReadOnlyList<string> Warnings,
    PerformanceMetricsDto Performance);

public enum PreviewChannel
{
    Leds,
    Lcd,
}
