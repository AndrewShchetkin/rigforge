using System.Buffers.Binary;
using System.Text;

namespace RigForge.Infrastructure.CrossPlatform.Smbus;

/// <summary>
/// Zero-dependency parser for SMBIOS Type 17 (Memory Device) structures from raw DMI table bytes.
/// Used by both Windows (<c>GetSystemFirmwareTable('RSMB')</c>) and Linux (<c>/sys/firmware/dmi/tables/DMI</c>).
/// </summary>
public static class SmbiosType17Parser
{
    public static IReadOnlyList<SmbiosMemoryStick> Parse(ReadOnlySpan<byte> dmi)
    {
        var sticks = new List<SmbiosMemoryStick>();
        var offset = 0;

        while (offset + 4 <= dmi.Length)
        {
            var type = dmi[offset];
            var formattedLength = dmi[offset + 1];
            if (formattedLength < 4 || offset + formattedLength > dmi.Length)
            {
                break;
            }

            var stringsStart = offset + formattedLength;
            var strings = ReadStructureStrings(dmi[stringsStart..], out var stringsBytes);
            var nextOffset = stringsStart + stringsBytes;

            if (type == 17 && formattedLength >= 0x1C)
            {
                var sizeRaw = BinaryPrimitives.ReadUInt16LittleEndian(dmi.Slice(offset + 0x0C, 2));
                if (sizeRaw is > 0 and not 0xFFFF)
                {
                    var sizeMb = (sizeRaw & 0x8000) != 0 ? (sizeRaw & 0x7FFF) / 1024 : (int)sizeRaw;
                    if (sizeRaw == 0x7FFF && formattedLength >= 0x20)
                    {
                        var extendedMb = (int)(BinaryPrimitives.ReadUInt32LittleEndian(dmi.Slice(offset + 0x1C, 4)) & 0x7FFF_FFFF);
                        if (extendedMb > 0)
                        {
                            sizeMb = extendedMb;
                        }
                    }

                    var speed = BinaryPrimitives.ReadUInt16LittleEndian(dmi.Slice(offset + 0x15, 2));
                    var deviceLocator = GetDmiString(strings, dmi[offset + 0x10]);
                    var bankLocator = GetDmiString(strings, dmi[offset + 0x11]);
                    var manufacturer = GetDmiString(strings, dmi[offset + 0x17]);
                    var serial = GetDmiString(strings, dmi[offset + 0x18]);
                    var partNumber = GetDmiString(strings, dmi[offset + 0x1A]).Trim();

                    if (partNumber.Length > 0)
                    {
                        sticks.Add(new SmbiosMemoryStick(
                            bankLocator,
                            deviceLocator,
                            manufacturer,
                            partNumber,
                            serial,
                            sizeMb,
                            speed));
                    }
                }
            }
            else if (type == 127)
            {
                break;
            }

            offset = nextOffset;
        }

        return sticks;
    }

    private static List<string> ReadStructureStrings(ReadOnlySpan<byte> tail, out int consumedBytes)
    {
        var result = new List<string>();
        var idx = 0;

        if (tail.Length >= 2 && tail[0] == 0 && tail[1] == 0)
        {
            consumedBytes = 2;
            return result;
        }

        while (idx < tail.Length)
        {
            var end = tail[idx..].IndexOf((byte)0);
            if (end < 0)
            {
                consumedBytes = tail.Length;
                return result;
            }

            result.Add(Encoding.ASCII.GetString(tail.Slice(idx, end)));
            idx += end + 1;

            if (idx < tail.Length && tail[idx] == 0)
            {
                idx++;
                break;
            }
        }

        consumedBytes = idx;
        return result;
    }

    private static string GetDmiString(List<string> strings, byte oneBasedIndex) =>
        oneBasedIndex > 0 && oneBasedIndex <= strings.Count ? strings[oneBasedIndex - 1] : string.Empty;
}
