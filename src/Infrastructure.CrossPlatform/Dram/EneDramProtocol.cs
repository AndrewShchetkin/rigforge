using System.Text;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Smbus;

namespace RigForge.Infrastructure.CrossPlatform.Dram;

public enum EneSlotProbeStatus
{
    Supported,
    NoDevice,
    SignatureMismatch,
    IoError,
}

public sealed record EneSlotProbeResult(byte Address, EneSlotProbeStatus Status, string? Signature, string Detail);

/// <summary>One ENE register write. <see cref="CacheIndex"/> selects the diff-cache column.</summary>
public readonly record struct EneRegisterWrite(int Slot, byte Address, ushort Register, byte Value, int CacheIndex);

/// <summary>
/// ENE RGB DRAM (G.Skill Trident Z / Crucial Ballistix / ASUS Aura DRAM) over SMBus.
/// Slaves are only <c>0x70..0x77</c> and <c>0x39..0x3C</c>. SPD <c>0x50..0x57</c> and PMIC <c>0x48..0x4F</c> are rejected.
/// The 16-bit register pointer is staged on SMBus register <c>0x00</c> (high byte, then low byte) and data is read from <c>0x81</c>.
/// A data-port write (<c>0x01</c>) happens only after the <c>AUDA</c> / <c>ENE </c> / <c>DIMM</c> signature matches.
/// Direct mode is <c>0x8021 = 0x01</c> then <c>0x8020 = 0x01</c>. LED bytes at <c>0x8100</c> are R, B, G.
/// </summary>
public static class EneDramProtocol
{
    public const byte AddressRegister = 0x00;
    public const byte DataRegister = 0x01;
    public const byte ValueRegister = 0x81;
    public const ushort RegisterName = 0x1000;
    public const int NameLength = 16;
    public const ushort RegisterDirect = 0x8020;
    public const ushort RegisterMode = 0x8021;
    public const byte Enable = 0x01;
    public const ushort RegisterColorBase = 0x8100;
    public const int LedsPerDimm = 8;
    public const int MaxLedsPerDimm = 16;

    public static ReadOnlySpan<byte> ProbeAddresses =>
    [
        0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77,
        0x39, 0x3A, 0x3B, 0x3C,
    ];

    public static int CacheWidth(int ledsPerStick) => 2 + (ledsPerStick * 3);

    public static int ColorRegisterCount(int ledsPerStick) => ledsPerStick * 3;

    public static void ValidateSlave(byte address)
    {
        SmbusAddressFirewall.ValidateAddress(address);
        var allowed = address is >= 0x70 and <= 0x77 or >= 0x39 and <= 0x3C;
        if (!allowed)
        {
            throw new ArgumentOutOfRangeException(
                nameof(address),
                $"0x{address:X2}",
                $"ENE DRAM safety guard blocked 0x{address:X2}. Only 0x70..0x77 and 0x39..0x3C are permitted (SPD 0x50..0x57 and PMIC 0x48..0x4F are strictly forbidden).");
        }
    }

    public static void ValidateWrite(byte address, ushort register, byte value)
    {
        ValidateSlave(address);
        if (register is RegisterMode or RegisterDirect)
        {
            if (value != Enable)
            {
                throw new ArgumentOutOfRangeException(nameof(value), $"0x{value:X2}", "ENE direct/mode registers accept only 0x01.");
            }

            return;
        }

        var colorLast = (ushort)(RegisterColorBase + (MaxLedsPerDimm * 3) - 1);
        if (register >= RegisterColorBase && register <= colorLast)
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            nameof(register),
            $"0x{register:X4}",
            $"ENE DRAM safety guard blocked a write to 0x{register:X4}.");
    }

    public static bool IsEneSignature(ReadOnlySpan<byte> name)
    {
        if (name.Length < 4)
        {
            return false;
        }

        if (name[0] == (byte)'A' && name[1] == (byte)'U' && name[2] == (byte)'D' && name[3] == (byte)'A')
        {
            return true;
        }

        if (name[0] == (byte)'E' && name[1] == (byte)'N' && name[2] == (byte)'E' && name[3] == (byte)' ')
        {
            return true;
        }

        return name[0] == (byte)'D' && name[1] == (byte)'I' && name[2] == (byte)'M' && name[3] == (byte)'M';
    }

    public static byte ExtractSmbusByte(ushort word)
    {
        var low = (byte)word;
        if (low != 0 || word == 0)
        {
            return low;
        }

        return (byte)(word >> 8);
    }

    public static bool TryReadRegister(ISmbusController bus, byte slave, ushort register, out byte value)
    {
        ArgumentNullException.ThrowIfNull(bus);
        value = 0;
        ValidateSlave(slave);
        if (!StagePointer(bus, slave, register))
        {
            return false;
        }

        if (!bus.TryReadWordData(slave, ValueRegister, out var word) || word == 0xFFFF)
        {
            return false;
        }

        value = ExtractSmbusByte(word);
        return true;
    }

    public static bool TryWriteRegister(ISmbusController bus, byte slave, ushort register, byte value)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ValidateWrite(slave, register, value);
        if (!StagePointer(bus, slave, register))
        {
            return false;
        }

        return bus.TryWriteByteData(slave, DataRegister, value);
    }

    public static EneSlotProbeResult ProbeAddress(ISmbusController bus, byte address)
    {
        ValidateSlave(address);
        Span<byte> name = stackalloc byte[NameLength];
        for (var i = 0; i < NameLength; i++)
        {
            if (!TryReadRegister(bus, address, (ushort)(RegisterName + i), out var decoded))
            {
                return new EneSlotProbeResult(
                    address,
                    i == 0 ? EneSlotProbeStatus.NoDevice : EneSlotProbeStatus.IoError,
                    Signature: null,
                    Detail: $"No ENE response at 0x{address:X2}");
            }

            name[i] = decoded;
        }

        var text = Encoding.ASCII.GetString(name).TrimEnd('\0', ' ');
        if (!IsEneSignature(name))
        {
            return new EneSlotProbeResult(
                address,
                EneSlotProbeStatus.SignatureMismatch,
                text,
                Detail: $"ENE signature mismatch at 0x{address:X2}: '{text}'");
        }

        return new EneSlotProbeResult(address, EneSlotProbeStatus.Supported, text, Detail: text);
    }

    /// <summary>
    /// Diff against <paramref name="registerCache"/>. Unchanged registers are omitted.
    /// Column 0 is mode <c>0x8021</c>, column 1 is direct <c>0x8020</c>, then R, B, G per LED.
    /// The cache is not updated here; the caller stores a value only after the bus accepts it.
    /// </summary>
    public static int BuildFrameWrites(
        ReadOnlySpan<byte> addresses,
        ReadOnlySpan<RgbColor> colors,
        int ledsPerStick,
        byte?[,] registerCache,
        Span<EneRegisterWrite> destination)
    {
        ArgumentNullException.ThrowIfNull(registerCache);
        if (ledsPerStick is < 1 or > MaxLedsPerDimm)
        {
            throw new ArgumentOutOfRangeException(nameof(ledsPerStick), ledsPerStick, "ENE LED count must be 1..16.");
        }

        var slots = addresses.Length;
        if (slots == 0)
        {
            return 0;
        }

        if (registerCache.GetLength(0) < slots || registerCache.GetLength(1) < CacheWidth(ledsPerStick))
        {
            throw new ArgumentException("The ENE register cache is smaller than the frame.", nameof(registerCache));
        }

        if (colors.Length < slots * ledsPerStick)
        {
            throw new ArgumentException($"Expected at least {slots * ledsPerStick} LED colors.", nameof(colors));
        }

        foreach (var address in addresses)
        {
            ValidateSlave(address);
        }

        var written = 0;
        for (var slot = 0; slot < slots; slot++)
        {
            AddIfChanged(addresses, registerCache, destination, ref written, slot, RegisterMode, Enable, cacheIndex: 0);
            AddIfChanged(addresses, registerCache, destination, ref written, slot, RegisterDirect, Enable, cacheIndex: 1);
            for (var led = 0; led < ledsPerStick; led++)
            {
                var color = colors[(slot * ledsPerStick) + led];
                var red = (ushort)(RegisterColorBase + (led * 3));
                AddIfChanged(addresses, registerCache, destination, ref written, slot, red, color.R, 2 + (led * 3));
                AddIfChanged(addresses, registerCache, destination, ref written, slot, (ushort)(red + 1), color.B, 2 + (led * 3) + 1);
                AddIfChanged(addresses, registerCache, destination, ref written, slot, (ushort)(red + 2), color.G, 2 + (led * 3) + 2);
            }
        }

        return written;
    }

    private static bool StagePointer(ISmbusController bus, byte slave, ushort register)
    {
        if (!bus.TryWriteByteData(slave, AddressRegister, (byte)(register >> 8)))
        {
            return false;
        }

        return bus.TryWriteByteData(slave, AddressRegister, (byte)register);
    }

    private static void AddIfChanged(
        ReadOnlySpan<byte> addresses,
        byte?[,] registerCache,
        Span<EneRegisterWrite> destination,
        ref int written,
        int slot,
        ushort register,
        byte value,
        int cacheIndex)
    {
        if (registerCache[slot, cacheIndex] == value)
        {
            return;
        }

        if (written >= destination.Length)
        {
            throw new ArgumentException("The destination buffer is too small for the ENE frame.", nameof(destination));
        }

        destination[written++] = new EneRegisterWrite(slot, addresses[slot], register, value, cacheIndex);
    }
}
