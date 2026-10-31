using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.Asus;

public enum AuraChannelKind
{
    /// <summary>Onboard LEDs plus 12 V RGB headers; fixed LED count.</summary>
    Mainboard,

    /// <summary>5 V ARGB header; LED count is not detectable.</summary>
    Addressable,
}

/// <param name="EffectChannel">Channel index used by mode (effect) commands.</param>
/// <param name="DirectChannel">Channel index used by direct-mode frames.</param>
public sealed record AuraChannel(AuraChannelKind Kind, byte EffectChannel, byte DirectChannel, int LedCount);

/// <summary>
/// ASUS Aura USB mainboard controller protocol (65-byte reports, report ID 0xEC), following OpenRGB's
/// AsusAuraUSBController / AsusAuraMainboardController.
/// </summary>
public static class AuraUsbProtocol
{
    public const int VendorId = 0x0B05;
    public const ushort UsagePage = 0xFF72;
    public const byte ReportId = 0xEC;
    public const int ReportLength = 65;
    public const int LedsPerPacket = 20;
    public const int MaxAddressableLeds = 120;
    public const byte MainboardDirectChannel = 0x04;
    public const byte DirectMode = 0xFF;

    private const byte DirectCommand = 0x40;
    private const byte ApplyFlag = 0x80;
    private const byte OffsetOverflowFlag = 0x10;

    /// <summary>Known mainboard controller PIDs (X570 era onwards).</summary>
    public static readonly IReadOnlySet<int> MainboardProductIds = new HashSet<int> { 0x18F3, 0x1939, 0x19AF, 0x1AA6, 0x1BED };

    public static byte[] FirmwareRequest() => Report(0x82);

    public static byte[] ConfigTableRequest() => Report(0xB0);

    /// <summary>Puts the ARGB headers into "Gen1" mode so any LED count can be streamed (sent by OpenRGB on init).</summary>
    public static byte[] SetGen1() => Report(0x52, 0x53, 0x00, 0x01);

    public static byte[] SetEffect(byte effectChannel, byte mode, bool shutdownEffect = false) =>
        Report(0x35, effectChannel, 0x00, (byte)(shutdownEffect ? 1 : 0), mode);

    public static bool IsFirmwareResponse(byte[] report) => report.Length > 17 && report[0] == ReportId && report[1] == 0x02;

    public static bool IsConfigTableResponse(byte[] report) => report.Length >= 64 && report[0] == ReportId && report[1] == 0x30;

    public static string ParseFirmware(byte[] report) =>
        System.Text.Encoding.ASCII.GetString(report, 2, 16).TrimEnd('\0', ' ');

    /// <summary>Builds the channel list from the 60-byte config table (response bytes 4..63).</summary>
    public static IReadOnlyList<AuraChannel> ParseChannels(byte[] configResponse, string firmware, int defaultArgbLeds)
    {
        var table = configResponse.AsSpan(4);
        int addressableHeaders = table[0x02];
        int mainboardLeds = table[0x1B];

        // AULA3-AR32 firmwares number the ARGB effect channels from 0 and put the mainboard after them.
        var argbFromZero = firmware.StartsWith("AULA3-AR32", StringComparison.Ordinal);
        var channels = new List<AuraChannel>();
        byte effectChannel = 0;

        if (mainboardLeds > 0)
        {
            channels.Add(new AuraChannel(AuraChannelKind.Mainboard, argbFromZero ? (byte)addressableHeaders : effectChannel, MainboardDirectChannel, mainboardLeds));
            if (!argbFromZero)
            {
                effectChannel++;
            }
        }

        for (var i = 0; i < addressableHeaders; i++)
        {
            channels.Add(new AuraChannel(AuraChannelKind.Addressable, effectChannel++, (byte)i, defaultArgbLeds));
        }

        return channels;
    }

    /// <summary>Direct-mode packets for one channel: 20 LEDs per packet, apply flag on the last one.</summary>
    public static IReadOnlyList<byte[]> DirectFrame(byte directChannel, ReadOnlySpan<RgbColor> colors)
    {
        var packets = new List<byte[]>((colors.Length + LedsPerPacket - 1) / LedsPerPacket);
        var command = directChannel;
        var offset = 0;
        var configured = 0;

        while (configured < colors.Length)
        {
            var inPacket = Math.Min(LedsPerPacket, colors.Length - configured);
            var first = configured;
            configured += inPacket;
            if (configured >= colors.Length)
            {
                command |= ApplyFlag;
            }

            var report = Report(DirectCommand, command, (byte)offset, (byte)inPacket);
            for (var i = 0; i < inPacket; i++)
            {
                var color = colors[first + i];
                report[5 + i * 3] = color.R;
                report[6 + i * 3] = color.G;
                report[7 + i * 3] = color.B;
            }

            packets.Add(report);

            offset += inPacket;
            if (configured > 255)
            {
                command |= OffsetOverflowFlag;
                offset = configured - 256;
            }
        }

        return packets;
    }

    private static byte[] Report(params ReadOnlySpan<byte> payload)
    {
        var report = new byte[ReportLength];
        report[0] = ReportId;
        payload.CopyTo(report.AsSpan(1));
        return report;
    }
}
