using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.Peripherals.QmkVial;

/// <summary>
/// QMK / VIA / VialRGB raw HID protocol (usage page <c>0xFF60</c>, usage <c>0x0061</c>).
/// The report is 33 bytes: report id <c>0x00</c> plus the 32-byte VIA payload.
/// Direct mode is volatile RAM only. Lighting-save (<c>0x09</c>) is never written.
/// </summary>
public static class QmkVialProtocol
{
    public const int VendorAgnostic = 0;
    public const ushort UsagePage = 0xFF60;
    public const ushort Usage = 0x0061;
    public const int ReportLength = 33;
    public const int PayloadLength = 32;
    public const int LedsPerPacket = 9;
    public const byte ReportId = 0x00;

    public const byte ViaGetProtocolVersion = 0x01;
    public const byte LightingSetValue = 0x07;
    public const byte LightingGetValue = 0x08;

    /// <summary>VIA lighting save. Never transmitted.</summary>
    public const byte LightingSave = 0x09;

    public const byte VialPrefix = 0xFE;
    public const byte VialGetKeyboardId = 0x00;
    public const byte VialGetSize = 0x01;

    public const byte VialRgbGetInfo = 0x40;
    public const byte VialRgbSetMode = 0x41;
    public const byte VialRgbDirectFastSet = 0x42;
    public const byte VialRgbGetNumberLeds = 0x43;
    public const byte VialRgbGetLedInfo = 0x44;

    /// <summary>VialRGB effect id for per-LED direct HSV. Kept in RAM by <c>rgb_matrix_mode_noeeprom</c>.</summary>
    public const ushort EffectDirect = 0x0001;

    /// <summary>QMK LED x coordinates are 0..224.</summary>
    public const float LedXMax = 224f;

    /// <summary>QMK LED y coordinates are 0..64.</summary>
    public const float LedYMax = 64f;

    public const int MaxLeds = 512;

    public static int PacketCount(int ledCount)
    {
        if (ledCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ledCount), ledCount, "LED count cannot be negative.");
        }

        if (ledCount == 0)
        {
            return 0;
        }

        return (ledCount + LedsPerPacket - 1) / LedsPerPacket;
    }

    /// <summary>RGB to the 8-bit HSV VialRGB puts on the wire (hue 0..255, not degrees).</summary>
    public static void ToQmkHsv(RgbColor color, out byte hue, out byte saturation, out byte value)
    {
        int r = color.R;
        int g = color.G;
        int b = color.B;
        var max = r > g ? (r > b ? r : b) : (g > b ? g : b);
        var min = r < g ? (r < b ? r : b) : (g < b ? g : b);
        value = (byte)max;
        if (max == 0)
        {
            hue = 0;
            saturation = 0;
            return;
        }

        var delta = max - min;
        saturation = (byte)(delta * 255 / max);
        if (delta == 0)
        {
            hue = 0;
            return;
        }

        int sector;
        if (max == r)
        {
            sector = (43 * (g - b)) / delta;
            if (sector < 0)
            {
                sector += 256;
            }
        }
        else if (max == g)
        {
            sector = 85 + (43 * (b - r) / delta);
        }
        else
        {
            sector = 171 + (43 * (r - g) / delta);
        }

        hue = (byte)sector;
    }

    /// <summary>Maps a QMK LED point (0..224, 0..64) onto a unit-square <see cref="LedPosition"/>.</summary>
    public static LedPosition ToLedPosition(byte x, byte y)
    {
        var nx = Math.Clamp(x / LedXMax, 0f, 1f);
        var ny = Math.Clamp(y / LedYMax, 0f, 1f);
        return new LedPosition(nx, ny);
    }

    public static void WriteGetProtocolVersion(Span<byte> packet)
    {
        Require(packet);
        packet.Clear();
        packet[0] = ReportId;
        packet[1] = ViaGetProtocolVersion;
    }

    public static void WriteGetVialKeyboardId(Span<byte> packet)
    {
        Require(packet);
        packet.Clear();
        packet[0] = ReportId;
        packet[1] = VialPrefix;
        packet[2] = VialGetKeyboardId;
    }

    public static void WriteGetVialRgbInfo(Span<byte> packet)
    {
        Require(packet);
        packet.Clear();
        packet[0] = ReportId;
        packet[1] = LightingGetValue;
        packet[2] = VialRgbGetInfo;
    }

    public static void WriteGetLedCount(Span<byte> packet)
    {
        Require(packet);
        packet.Clear();
        packet[0] = ReportId;
        packet[1] = LightingGetValue;
        packet[2] = VialRgbGetNumberLeds;
    }

    public static void WriteGetLedInfo(ushort ledIndex, Span<byte> packet)
    {
        Require(packet);
        packet.Clear();
        packet[0] = ReportId;
        packet[1] = LightingGetValue;
        packet[2] = VialRgbGetLedInfo;
        packet[3] = (byte)ledIndex;
        packet[4] = (byte)(ledIndex >> 8);
    }

    /// <summary>
    /// Enters VialRGB direct mode. Bytes are
    /// <c>[0x00, 0x07, 0x41, mode=1, 0x00, speed, hue, sat, maxBrightness]</c>.
    /// This is <c>vialrgb_set_mode</c> and does not call lighting-save.
    /// </summary>
    public static void WriteDirectMode(byte maxBrightness, Span<byte> packet)
    {
        Require(packet);
        packet.Clear();
        packet[0] = ReportId;
        packet[1] = LightingSetValue;
        packet[2] = VialRgbSetMode;
        packet[3] = (byte)EffectDirect;
        packet[4] = (byte)(EffectDirect >> 8);
        packet[5] = 0x00;
        packet[6] = 0x00;
        packet[7] = 0x00;
        packet[8] = maxBrightness;
    }

    /// <summary>
    /// One direct HSV packet, up to 9 LEDs (27 bytes) inside the 33-byte report:
    /// <c>[0x00, 0x07, 0x42, startLo, startHi, count, H, S, V, ...]</c>.
    /// </summary>
    public static void WriteDirectPacket(ushort startLed, ReadOnlySpan<RgbColor> colors, Span<byte> packet)
    {
        Require(packet);
        if (colors.Length > LedsPerPacket)
        {
            throw new ArgumentOutOfRangeException(nameof(colors), colors.Length, $"A VialRGB direct packet carries at most {LedsPerPacket} LEDs.");
        }

        packet.Clear();
        packet[0] = ReportId;
        packet[1] = LightingSetValue;
        packet[2] = VialRgbDirectFastSet;
        packet[3] = (byte)startLed;
        packet[4] = (byte)(startLed >> 8);
        packet[5] = (byte)colors.Length;
        for (var i = 0; i < colors.Length; i++)
        {
            ToQmkHsv(colors[i], out var hue, out var saturation, out var value);
            var offset = 6 + (i * 3);
            packet[offset] = hue;
            packet[offset + 1] = saturation;
            packet[offset + 2] = value;
        }
    }

    public static bool IsViaProtocolResponse(ReadOnlySpan<byte> response) =>
        response.Length >= 2 && (response[1] == ViaGetProtocolVersion || response[0] == ViaGetProtocolVersion);

    /// <summary>
    /// Reads the VialRGB-compiled flag from a <c>vial_get_keyboard_id</c> reply (payload byte 12, bit 0).
    /// Returns false when the reply is not a Vial keyboard-id payload.
    /// </summary>
    public static bool TryGetVialRgbCompiledFlag(ReadOnlySpan<byte> response, out bool compiled)
    {
        compiled = false;
        var payload = response.Length > 0 && response[0] == ReportId ? response[1..] : response;
        if (payload.Length < 13 || payload[0] == VialPrefix)
        {
            return false;
        }

        var version = payload[0] | (payload[1] << 8) | (payload[2] << 16) | (payload[3] << 24);
        if (version is <= 0 or > 32)
        {
            return false;
        }

        compiled = (payload[12] & 0x01) != 0;
        return true;
    }

    public static bool TryParseVialRgbInfo(ReadOnlySpan<byte> response, out ushort version, out byte maxBrightness)
    {
        version = 0;
        maxBrightness = 0;
        if (!TryLocate(response, LightingGetValue, VialRgbGetInfo, out var data))
        {
            return false;
        }

        if (response.Length < data + 3)
        {
            return false;
        }

        version = (ushort)(response[data] | (response[data + 1] << 8));
        maxBrightness = response[data + 2];
        return version >= 1;
    }

    public static bool TryParseLedCount(ReadOnlySpan<byte> response, out int ledCount)
    {
        ledCount = 0;
        if (!TryLocate(response, LightingGetValue, VialRgbGetNumberLeds, out var data) || response.Length < data + 2)
        {
            return false;
        }

        ledCount = response[data] | (response[data + 1] << 8);
        return ledCount is > 0 and <= MaxLeds;
    }

    public static bool TryParseLedInfo(ReadOnlySpan<byte> response, out byte x, out byte y, out byte flags)
    {
        x = 0;
        y = 0;
        flags = 0;
        if (!TryLocate(response, LightingGetValue, VialRgbGetLedInfo, out var data) || response.Length < data + 3)
        {
            return false;
        }

        x = response[data];
        y = response[data + 1];
        flags = response[data + 2];
        return true;
    }

    private static bool TryLocate(ReadOnlySpan<byte> response, byte command, byte subcommand, out int dataIndex)
    {
        if (response.Length >= 4 && response[1] == command && response[2] == subcommand)
        {
            dataIndex = 3;
            return true;
        }

        if (response.Length >= 3 && response[0] == command && response[1] == subcommand)
        {
            dataIndex = 2;
            return true;
        }

        dataIndex = 0;
        return false;
    }

    private static void Require(Span<byte> packet)
    {
        if (packet.Length < ReportLength)
        {
            throw new ArgumentException($"A QMK/Vial report needs at least {ReportLength} bytes.", nameof(packet));
        }
    }
}
