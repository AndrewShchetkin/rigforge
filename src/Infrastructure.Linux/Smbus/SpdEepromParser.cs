using RigForge.Infrastructure.CrossPlatform.Smbus;

namespace RigForge.Infrastructure.Linux.Smbus;

/// <summary>
/// Reads a DIMM part number from a cached SPD EEPROM image. This parses bytes already exposed by sysfs
/// (<c>ee1004</c> / <c>spd5118</c>) and does not open <c>/dev/i2c-*</c>.
/// DDR4 part number: bytes <c>0x149..0x15C</c>. DDR5 part number: bytes <c>0x209..0x226</c>.
/// </summary>
internal static class SpdEepromParser
{
    public const int Ddr4PartNumberOffset = 0x149;

    public const int Ddr4PartNumberLength = 0x15C - 0x149 + 1;

    public const int Ddr5PartNumberOffset = 0x209;

    public const int Ddr5PartNumberLength = 0x226 - 0x209 + 1;

    public static string ReadDdr4PartNumber(ReadOnlySpan<byte> eeprom) =>
        ReadAt(eeprom, Ddr4PartNumberOffset, Ddr4PartNumberLength);

    public static string ReadDdr5PartNumber(ReadOnlySpan<byte> eeprom) =>
        ReadAt(eeprom, Ddr5PartNumberOffset, Ddr5PartNumberLength);

    /// <summary>
    /// DDR5 images are longer than 512 bytes, so a buffer that covers <c>0x226</c> is read as DDR5.
    /// Shorter images are read as DDR4.
    /// </summary>
    public static string? ReadPartNumber(ReadOnlySpan<byte> eeprom)
    {
        if (eeprom.Length >= Ddr5PartNumberOffset + Ddr5PartNumberLength)
        {
            var ddr5 = ReadDdr5PartNumber(eeprom);
            if (ddr5.Length > 0)
            {
                return ddr5;
            }
        }

        if (eeprom.Length >= Ddr4PartNumberOffset + Ddr4PartNumberLength)
        {
            var ddr4 = ReadDdr4PartNumber(eeprom);
            if (ddr4.Length > 0)
            {
                return ddr4;
            }
        }

        return null;
    }

    public static SmbiosMemoryStick? ToStick(string deviceLocator, ReadOnlySpan<byte> eeprom)
    {
        var part = ReadPartNumber(eeprom);
        if (string.IsNullOrWhiteSpace(part))
        {
            return null;
        }

        return new SmbiosMemoryStick(deviceLocator, deviceLocator, string.Empty, part, string.Empty, 0, 0);
    }

    /// <summary><c>bus-0050</c> .. <c>bus-0057</c> SPD EEPROM device directory names.</summary>
    public static bool IsSpdDeviceName(string name)
    {
        var dash = name.LastIndexOf('-');
        if (dash < 0 || dash >= name.Length - 1)
        {
            return false;
        }

        var address = name[(dash + 1)..];
        return address.Length == 4
            && address.StartsWith("005", StringComparison.Ordinal)
            && address[3] is >= '0' and <= '7';
    }

    private static string ReadAt(ReadOnlySpan<byte> eeprom, int offset, int length)
    {
        if (offset < 0 || length <= 0 || eeprom.Length < offset + length)
        {
            return string.Empty;
        }

        return ReadAscii(eeprom.Slice(offset, length));
    }

    private static string ReadAscii(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.Length;
        while (end > 0 && bytes[end - 1] is 0 or 0x20)
        {
            end--;
        }

        if (end == 0)
        {
            return string.Empty;
        }

        Span<char> chars = stackalloc char[end];
        var count = 0;
        for (var i = 0; i < end; i++)
        {
            var value = bytes[i];
            if (value is < 0x20 or > 0x7E)
            {
                break;
            }

            chars[count++] = (char)value;
        }

        return count == 0 ? string.Empty : new string(chars[..count]);
    }
}
