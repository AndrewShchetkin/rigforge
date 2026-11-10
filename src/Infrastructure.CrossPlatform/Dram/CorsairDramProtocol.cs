using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Smbus;

namespace RigForge.Infrastructure.CrossPlatform.Dram;

public enum CorsairSlotProbeStatus
{
    Supported,
    NoDevice,
    SignatureMismatch,
    IoError,
}

public sealed record CorsairSlotProbeResult(int SlotIndex, byte Address, CorsairSlotProbeStatus Status, byte Vendor, byte Model, string Detail);

/// <summary>
/// One Corsair DRAM register write. <see cref="Remember"/> is false for the apply pulse,
/// which must be sent again whenever a color actually changes.
/// </summary>
public readonly record struct CorsairRegisterWrite(int Slot, byte Address, byte Register, byte Value, bool Remember);

/// <summary>
/// Corsair Vengeance / Dominator RGB DRAM on SMBus <c>0x58..0x5F</c> (strictly above SPD <c>0x50..0x57</c>).
/// Vendor <c>0x43 == 0x1B</c> and model <c>0x44 == 0x04</c> are read before any direct-mode write.
/// Direct colors use <c>0x31</c> to enable and <c>0x32</c> to apply. Both values are volatile.
/// </summary>
public static class CorsairDramProtocol
{
    public const byte BaseAddress = 0x58;
    public const byte LastAddress = 0x5F;
    public const int MaxSlots = 8;
    public const byte RegisterVendor = 0x43;
    public const byte VendorId = 0x1B;
    public const byte RegisterModel = 0x44;
    public const byte ModelId = 0x04;
    public const byte RegisterDirect = 0x31;
    public const byte RegisterApply = 0x32;
    public const byte DirectEnable = 0x01;
    public const byte ApplyColors = 0x01;
    public const byte ColorBase = 0x60;
    public const int LedsPerDimm = 10;
    public const int DominatorLedsPerDimm = 12;

    public static int ColorLast(int ledsPerStick) => ColorBase + (ledsPerStick * 3) - 1;

    public static byte SlotAddress(int slotIndex)
    {
        if (slotIndex is < 0 or >= MaxSlots)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "Corsair DRAM slot index must be in 0..7.");
        }

        return (byte)(BaseAddress + slotIndex);
    }

    public static void ValidateSlave(byte address)
    {
        SmbusAddressFirewall.ValidateAddress(address);
        if (address is < BaseAddress or > LastAddress)
        {
            throw new ArgumentOutOfRangeException(
                nameof(address),
                $"0x{address:X2}",
                $"Corsair DRAM safety guard blocked 0x{address:X2}. Only 0x58..0x5F are permitted (SPD 0x50..0x57 and PMIC 0x48..0x4F are strictly forbidden).");
        }
    }

    public static void ValidateRead(byte address, byte register)
    {
        ValidateSlave(address);
        if (register is not (RegisterVendor or RegisterModel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(register),
                $"0x{register:X2}",
                "Corsair DRAM reads are limited to the vendor (0x43) and model (0x44) registers.");
        }
    }

    public static void ValidateWrite(byte address, byte register, byte value)
    {
        ValidateSlave(address);
        if (register is RegisterDirect or RegisterApply)
        {
            if (value != DirectEnable)
            {
                throw new ArgumentOutOfRangeException(nameof(value), $"0x{value:X2}", "Corsair direct/apply registers accept only 0x01.");
            }

            return;
        }

        var last = ColorLast(DominatorLedsPerDimm);
        if (register >= ColorBase && register <= last)
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            nameof(register),
            $"0x{register:X2}",
            $"Corsair DRAM safety guard blocked a write to register 0x{register:X2}.");
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

    public static int LedCountForPart(string? partNumber)
    {
        if (string.IsNullOrWhiteSpace(partNumber))
        {
            return LedsPerDimm;
        }

        var part = partNumber.Trim();
        if (part.Contains("DOMINATOR", StringComparison.OrdinalIgnoreCase)
            || part.StartsWith("CMT", StringComparison.OrdinalIgnoreCase)
            || part.StartsWith("CMP", StringComparison.OrdinalIgnoreCase))
        {
            return DominatorLedsPerDimm;
        }

        return LedsPerDimm;
    }

    public static bool TryReadIdentification(ISmbusController bus, byte address, byte register, out byte value)
    {
        ArgumentNullException.ThrowIfNull(bus);
        value = 0;
        ValidateRead(address, register);
        if (!bus.TryReadWordData(address, register, out var word) || word == 0xFFFF)
        {
            return false;
        }

        value = ExtractSmbusByte(word);
        return true;
    }

    public static bool TryWriteRegister(ISmbusController bus, byte address, byte register, byte value)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ValidateWrite(address, register, value);
        return bus.TryWriteByteData(address, register, value);
    }

    public static CorsairSlotProbeResult ProbeSlot(ISmbusController bus, int slotIndex)
    {
        var address = SlotAddress(slotIndex);
        ValidateSlave(address);
        if (!TryReadIdentification(bus, address, RegisterVendor, out var vendor))
        {
            return new CorsairSlotProbeResult(slotIndex, address, CorsairSlotProbeStatus.NoDevice, 0, 0, $"No response at 0x{address:X2}");
        }

        if (!TryReadIdentification(bus, address, RegisterModel, out var model))
        {
            return new CorsairSlotProbeResult(slotIndex, address, CorsairSlotProbeStatus.IoError, vendor, 0, $"Failed to read model at 0x{address:X2}");
        }

        if (vendor != VendorId || model != ModelId)
        {
            return new CorsairSlotProbeResult(
                slotIndex,
                address,
                CorsairSlotProbeStatus.SignatureMismatch,
                vendor,
                model,
                $"Corsair id mismatch at 0x{address:X2}: vendor 0x{vendor:X2}, model 0x{model:X2}");
        }

        return new CorsairSlotProbeResult(slotIndex, address, CorsairSlotProbeStatus.Supported, vendor, model, "Corsair RGB DRAM");
    }

    /// <summary>
    /// Diff-cached direct frame. Identical colors produce zero writes, including the apply pulse.
    /// </summary>
    public static int BuildFrameWrites(
        ReadOnlySpan<byte> addresses,
        ReadOnlySpan<RgbColor> colors,
        int ledsPerStick,
        byte?[,] registerCache,
        Span<CorsairRegisterWrite> destination)
    {
        ArgumentNullException.ThrowIfNull(registerCache);
        if (ledsPerStick is < 1 or > DominatorLedsPerDimm)
        {
            throw new ArgumentOutOfRangeException(nameof(ledsPerStick));
        }

        var slots = addresses.Length;
        if (slots == 0)
        {
            return 0;
        }

        if (registerCache.GetLength(0) < slots || registerCache.GetLength(1) < 256)
        {
            throw new ArgumentException("The Corsair register cache is smaller than the frame.", nameof(registerCache));
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
        var before = written;
        for (var slot = 0; slot < slots; slot++)
        {
            AddIfChanged(addresses, registerCache, destination, ref written, slot, RegisterDirect, DirectEnable, remember: true);
            for (var led = 0; led < ledsPerStick; led++)
            {
                var color = colors[(slot * ledsPerStick) + led];
                var red = (byte)(ColorBase + (led * 3));
                AddIfChanged(addresses, registerCache, destination, ref written, slot, red, color.R, remember: true);
                AddIfChanged(addresses, registerCache, destination, ref written, slot, (byte)(red + 1), color.G, remember: true);
                AddIfChanged(addresses, registerCache, destination, ref written, slot, (byte)(red + 2), color.B, remember: true);
            }
        }

        if (written > before)
        {
            for (var slot = 0; slot < slots; slot++)
            {
                Append(destination, ref written, new CorsairRegisterWrite(slot, addresses[slot], RegisterApply, ApplyColors, Remember: false));
            }
        }

        return written;
    }

    private static void AddIfChanged(
        ReadOnlySpan<byte> addresses,
        byte?[,] registerCache,
        Span<CorsairRegisterWrite> destination,
        ref int written,
        int slot,
        byte register,
        byte value,
        bool remember)
    {
        if (registerCache[slot, register] == value)
        {
            return;
        }

        Append(destination, ref written, new CorsairRegisterWrite(slot, addresses[slot], register, value, remember));
    }

    private static void Append(Span<CorsairRegisterWrite> destination, ref int written, CorsairRegisterWrite write)
    {
        if (written >= destination.Length)
        {
            throw new ArgumentException("The destination buffer is too small for the Corsair frame.", nameof(destination));
        }

        destination[written++] = write;
    }
}
