using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.Nzxt;

public sealed record KrakenStatus(float LiquidTemperature, int PumpRpm, int PumpDuty, int FanRpm, int FanDuty);

public sealed record SmartDeviceFanStatus(int Channel, bool Connected, int Rpm, int Duty);

/// <summary>
/// Packet builders and parsers for NZXT HID devices (64-byte reports, report ID = command byte).
/// Byte layouts follow liquidctl (kraken3.py, smart_device.py) and OpenRGB (NZXTHue2Controller).
/// Responses echo the request with command + 1 (0x10 → 0x11, 0x30 → 0x31, 0x74 → 0x75, ...).
/// </summary>
public static class NzxtProtocol
{
    public const int ReportLength = 64;

    /// <summary>Kraken duty profiles hold one duty per liquid temperature from 20 °C to 59 °C.</summary>
    public const int ProfileFirstTemperature = 20;
    public const int ProfileLastTemperature = 59;
    public const int ProfileLength = ProfileLastTemperature - ProfileFirstTemperature + 1;

    public const int MaxLedsPerChannel = 40;
    public const int LedsPerPacket = 20;
    public const int MaxAccessoriesPerChannel = 6;

    /// <summary>Status report interval for 0.5 s, computed like liquidctl: 1 + round((secs - 0.5) / 0.25).</summary>
    private const byte StatusInterval = 1;

    public static byte[] FirmwareRequest() => Report(0x10, 0x01);

    public static byte[] LightingInfoRequest() => Report(0x20, 0x03);

    public static byte[] KrakenSetStatusInterval() => Report(0x70, 0x02, 0x01, 0xB8, StatusInterval);

    public static byte[] KrakenFinishInitialization() => Report(0x70, 0x01);

    public static byte[] KrakenStatusRequest() => Report(0x74, 0x01);

    public static byte[] LcdInfoRequest() => Report(0x30, 0x01);

    public static byte[] LcdSettings(int brightness, int orientation) =>
        Report(0x30, 0x02, 0x01, (byte)Math.Clamp(brightness, 0, 100), 0x00, 0x00, 0x01, (byte)Math.Clamp(orientation, 0, 3));

    public static byte[] SmartDeviceSetStatusInterval() =>
        Report(0x60, 0x02, 0x01, 0xE8, StatusInterval, 0x01, 0xE8, StatusInterval);

    public static byte[] SmartDeviceFinishInitialization() => Report(0x60, 0x03);

    /// <summary>Kraken channel profile: [0x72, channel header (3 bytes), 40 duties].</summary>
    public static byte[] KrakenDutyProfile(ReadOnlySpan<byte> channelHeader, ReadOnlySpan<byte> duties)
    {
        if (duties.Length != ProfileLength)
        {
            throw new ArgumentException($"A Kraken profile needs exactly {ProfileLength} duties.", nameof(duties));
        }

        var report = new byte[ReportLength];
        report[0] = 0x72;
        channelHeader.CopyTo(report.AsSpan(1));
        duties.CopyTo(report.AsSpan(1 + channelHeader.Length));
        return report;
    }

    /// <summary>
    /// A fixed duty for host-side control that still escalates inside the firmware when the coolant gets hot,
    /// so the pump and fans stay safe even if the daemon dies while holding a low duty.
    /// </summary>
    public static byte[] FixedDutyWithSafetyFloor(int duty, IReadOnlyList<(int Temperature, int Duty)> safetyFloor)
    {
        var floor = InterpolateProfile(safetyFloor, 0, 100);
        var duties = new byte[ProfileLength];
        for (var i = 0; i < ProfileLength; i++)
        {
            duties[i] = (byte)Math.Clamp(Math.Max(duty, floor[i]), 0, 100);
        }

        return duties;
    }

    /// <summary>Interpolates a liquid-temperature curve into the 40-entry profile; the last entry is forced to 100 %.</summary>
    public static byte[] InterpolateProfile(IReadOnlyList<(int Temperature, int Duty)> points, int minDuty, int maxDuty)
    {
        var sorted = points.OrderBy(p => p.Temperature).ToArray();
        var duties = new byte[ProfileLength];
        for (var i = 0; i < ProfileLength; i++)
        {
            var temperature = ProfileFirstTemperature + i;
            duties[i] = (byte)Math.Clamp((int)Math.Round(Interpolate(sorted, temperature)), minDuty, maxDuty);
        }

        duties[^1] = 100;
        return duties;
    }

    public static byte[] SmartDeviceFixedDuty(int channel, int duty)
    {
        var report = Report(0x62, 0x01, (byte)(1 << channel));
        report[3 + channel] = (byte)Math.Clamp(duty, 0, 100);
        return report;
    }

    /// <summary>Direct-mode color packet for one group of up to 20 LEDs (colors are sent in GRB order).</summary>
    public static byte[] Hue2Direct(int channel, int group, ReadOnlySpan<RgbColor> colors)
    {
        var report = new byte[ReportLength];
        report[0] = 0x22;
        report[1] = (byte)(0x10 | group);
        report[2] = (byte)(1 << channel);
        var count = Math.Min(colors.Length, LedsPerPacket);
        for (var i = 0; i < count; i++)
        {
            report[4 + i * 3] = colors[i].G;
            report[5 + i * 3] = colors[i].R;
            report[6 + i * 3] = colors[i].B;
        }

        return report;
    }

    /// <summary>Commits the direct-mode colors of a channel.</summary>
    public static byte[] Hue2Apply(int channel) =>
        Report(0x22, 0xA0, (byte)(1 << channel), 0x00, 0x01, 0x00, 0x00, 0x28, 0x00, 0x00, 0x80, 0x00, 0x32, 0x00, 0x00, 0x01);

    /// <summary>All packets for one channel frame: one or two color groups followed by the apply packet.</summary>
    public static IReadOnlyList<byte[]> Hue2Frame(int channel, ReadOnlySpan<RgbColor> colors)
    {
        var count = Math.Min(colors.Length, MaxLedsPerChannel);
        var packets = new List<byte[]>(3) { Hue2Direct(channel, 0, colors[..Math.Min(count, LedsPerPacket)]) };
        if (count > LedsPerPacket)
        {
            packets.Add(Hue2Direct(channel, 1, colors[LedsPerPacket..count]));
        }

        packets.Add(Hue2Apply(channel));
        return packets;
    }

    public static bool IsResponse(byte[] report, byte command, byte subCommand) =>
        report.Length >= 2 && report[0] == command && report[1] == subCommand;

    public static string ParseFirmware(byte[] report) =>
        report.Length > 0x13 ? $"{report[0x11]}.{report[0x12]}.{report[0x13]}" : "unknown";

    public static KrakenStatus? ParseKrakenStatus(byte[] report)
    {
        if (report.Length < 26 || report[0] != 0x75)
        {
            return null;
        }

        // 0xFFFF in the temperature bytes is a known firmware fault (liquidctl issue #172).
        if (report[15] == 0xFF && report[16] == 0xFF)
        {
            return null;
        }

        return new KrakenStatus(
            report[15] + report[16] / 10f,
            report[17] | report[18] << 8,
            report[19],
            report[23] | report[24] << 8,
            report[25]);
    }

    public static (int Brightness, int Orientation) ParseLcdInfo(byte[] report) =>
        report.Length > 0x1A ? (report[0x18], report[0x1A]) : (50, 0);

    /// <summary>Accessory ids per LED channel from a [0x21, 0x03] report.</summary>
    public static IReadOnlyList<IReadOnlyList<byte>> ParseAccessories(byte[] report, int maxChannels)
    {
        if (report.Length < 15)
        {
            return [];
        }

        var channels = Math.Min(report[14], maxChannels);
        var result = new List<IReadOnlyList<byte>>(channels);
        for (var channel = 0; channel < channels; channel++)
        {
            var accessories = new List<byte>();
            for (var slot = 0; slot < MaxAccessoriesPerChannel; slot++)
            {
                var offset = 15 + channel * MaxAccessoriesPerChannel + slot;
                if (offset >= report.Length || report[offset] == 0)
                {
                    break;
                }

                accessories.Add(report[offset]);
            }

            result.Add(accessories);
        }

        return result;
    }

    /// <summary>
    /// Number of fan channels announced in byte 14 of a [0x67, 0x02] report (5 on the Control Hub), or
    /// <paramref name="fallback"/> when the value is implausible. Only ever widens the catalog value.
    /// </summary>
    public static int FanChannelCount(byte[] statusReport, int fallback) =>
        IsResponse(statusReport, 0x67, 0x02) && statusReport.Length > 14 && statusReport[14] > fallback && statusReport[14] <= 8
            ? statusReport[14]
            : fallback;

    /// <summary>Fan status from a periodic [0x67, 0x02] report.</summary>
    public static IReadOnlyList<SmartDeviceFanStatus> ParseSmartDeviceFans(byte[] report, int fanChannels)
    {
        if (!IsResponse(report, 0x67, 0x02) || report.Length < 40 + fanChannels)
        {
            return [];
        }

        var result = new List<SmartDeviceFanStatus>(fanChannels);
        for (var i = 0; i < fanChannels; i++)
        {
            result.Add(new SmartDeviceFanStatus(
                i,
                Connected: report[16 + i] != 0,
                Rpm: report[24 + i * 2] | report[25 + i * 2] << 8,
                Duty: report[40 + i]));
        }

        return result;
    }

    private static float Interpolate(IReadOnlyList<(int Temperature, int Duty)> sorted, int temperature)
    {
        if (sorted.Count == 0)
        {
            return 100;
        }

        if (temperature <= sorted[0].Temperature)
        {
            return sorted[0].Duty;
        }

        for (var i = 1; i < sorted.Count; i++)
        {
            if (temperature <= sorted[i].Temperature)
            {
                var (t0, d0) = sorted[i - 1];
                var (t1, d1) = sorted[i];
                return t1 == t0 ? d1 : d0 + (d1 - d0) * (temperature - t0) / (float)(t1 - t0);
            }
        }

        return sorted[^1].Duty;
    }

    private static byte[] Report(params ReadOnlySpan<byte> bytes)
    {
        var report = new byte[ReportLength];
        bytes.CopyTo(report);
        return report;
    }
}
