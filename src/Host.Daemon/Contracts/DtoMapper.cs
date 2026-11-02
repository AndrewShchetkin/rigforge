using RigForge.Application.Cooling;
using RigForge.Application.Effects;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Host.Daemon.Contracts;

/// <summary>Explicit domain → DTO projections (no reflection-based mapper: cheap, trimming/AOT friendly).</summary>
internal static class DtoMapper
{
    private static readonly DeviceCapabilities[] CapabilityFlags =
        [DeviceCapabilities.Rgb, DeviceCapabilities.FanControl, DeviceCapabilities.Sensors, DeviceCapabilities.Lcd];

    public static DeviceDto ToDto(IHardwareDevice device) => new(
        device.Id,
        device.Name,
        device.Vendor,
        CapabilityFlags.Where(flag => device.Capabilities.HasFlag(flag)).Select(flag => flag.ToString()).ToArray(),
        device.Properties,
        device is IRgbDevice rgb && device.Capabilities.HasFlag(DeviceCapabilities.Rgb) ? rgb.Zones.Select(ToDto).ToArray() : [],
        device is IFanController fans && device.Capabilities.HasFlag(DeviceCapabilities.FanControl) ? fans.Channels.Select(ToDto).ToArray() : [],
        device is IAioLcdDevice lcd && device.Capabilities.HasFlag(DeviceCapabilities.Lcd)
            ? new LcdInfoDto(lcd.Lcd.Width, lcd.Lcd.Height, lcd.Lcd.IsCircular, lcd.Lcd.MaxFramesPerSecond)
            : null);

    public static RgbZoneDto ToDto(RgbZone zone) => new(
        zone.Id,
        zone.Name,
        zone.Topology,
        zone.LedCount,
        zone.IsResizable,
        zone.MaxLedCount,
        zone.Leds.Select(led => new LedPositionDto(led.X, led.Y)).ToArray());

    public static FanChannelDto ToDto(FanChannel channel) => new(
        channel.Id, channel.Name, channel.Kind, channel.MinDutyPercent, channel.MaxDutyPercent, channel.SpeedSensorId);

    public static TelemetryDto ToDto(TelemetrySnapshot snapshot) => new(
        snapshot.Timestamp,
        snapshot.Readings.Select(r => new SensorReadingDto(r.Id, r.Name, r.Kind, r.Role, r.Value, r.Unit, r.Timestamp)).ToArray());

    public static FanStatusDto ToDto(FanChannelStatus status) => new(
        status.CurveId, status.DeviceId, status.ChannelId, status.Temperature, status.TargetDuty, status.AppliedDuty, status.Failsafe, status.Message);

    public static EffectDescriptorDto ToDto(EffectDescriptor effect) => new(
        effect.Id, effect.Name, effect.Description, effect.IsAnimated,
        effect.Parameters.Select(p => new EffectParameterDefinitionDto(
            p.Key, p.Label, p.Kind, p.DefaultValue, p.Min, p.Max, p.Step, p.Unit, p.MinColors, p.MaxColors)).ToArray());

    public static LedPreviewDto ToDto(IReadOnlyList<ZonePreview> preview) =>
        new(preview.Select(z => new ZonePreviewDto(z.DeviceId, z.ZoneId, z.Rgb)).ToArray());
}
