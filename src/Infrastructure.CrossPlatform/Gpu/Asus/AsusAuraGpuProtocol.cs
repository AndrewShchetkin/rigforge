using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.Gpu.Asus;

/// <summary>
/// ENE-based ASUS Aura GPU controller (<c>0x67</c>). 16-bit internal registers are reached through a pointer port
/// (<c>0x00</c>, big-endian) and a single-byte data port (<c>0x01</c>); colors stream through the block port
/// (<c>0x03</c>). Only volatile registers are ever addressed: there is no flash / EEPROM save command.
/// </summary>
internal static class AsusAuraGpuProtocol
{
    public const byte DefaultSlaveAddress = 0x67;
    public const int MaxLeds = 30;
    public const int MaxChunkPayload = 31;

    public const ushort DeviceName = 0x1000;
    public const int DeviceNameLength = 16;
    public const ushort ConfigTableLedCount = 0x1C03;
    public const ushort DirectAccess = 0x8020;
    public const ushort Apply = 0x80A0;
    public const ushort ColorCtlV2 = 0x8100;

    private const ushort DirectionRegister = 0x8024;
    private const ushort CommitRegister = 0x802F;

    private static ReadOnlySpan<byte> PointerPort => [0x00];

    private static ReadOnlySpan<byte> DataPort => [0x01];

    private static ReadOnlySpan<byte> SpeedModePort => [0x02];

    private static ReadOnlySpan<byte> BlockColorPort => [0x03];

    private static ReadOnlySpan<byte> ReadPort => [0x81];

    /// <summary>True for controller names that identify an ASUS Aura GPU (<c>AUMA…</c> / <c>AURA…</c>).</summary>
    public static bool IsAuraGpuSignature(string name) =>
        name.StartsWith("AUMA", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("AURA", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Pure read-only ENE presence check: a one-byte raw I2C read (or, when the transport refuses bare reads, a one-byte
    /// register read of the data-port address). Neither sends a register pointer or any data write, so a missing or
    /// non-ENE chip never sees a write when the address does not acknowledge.
    /// </summary>
    public static bool TryProbePresenceReadOnly(IGpuI2cBus bus, byte slaveAddress)
    {
        Span<byte> probe = stackalloc byte[1];
        return bus.TryReadRaw(slaveAddress, probe)
            || bus.TryReadRegisterBlock(slaveAddress, ReadPort, probe);
    }

    public static bool TrySelectRegister(IGpuI2cBus bus, byte slaveAddress, ushort register)
    {
        Span<byte> pointer = [(byte)(register >> 8), (byte)register];
        return bus.TryWriteRegisterBlock(slaveAddress, PointerPort, pointer);
    }

    public static bool TryWriteRegister(IGpuI2cBus bus, byte slaveAddress, ushort register, byte value)
    {
        Span<byte> data = [value];
        return TrySelectRegister(bus, slaveAddress, register)
            && bus.TryWriteRegisterBlock(slaveAddress, DataPort, data);
    }

    public static bool TryReadRegister(IGpuI2cBus bus, byte slaveAddress, ushort register, out byte value)
    {
        Span<byte> data = stackalloc byte[1];
        if (TrySelectRegister(bus, slaveAddress, register) && bus.TryReadRegisterBlock(slaveAddress, ReadPort, data))
        {
            value = data[0];
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>Reads the 16-byte ASCII controller name (printable bytes only, trailing NUL / whitespace trimmed).</summary>
    public static bool TryReadControllerName(IGpuI2cBus bus, byte slaveAddress, out string name)
    {
        Span<char> chars = stackalloc char[DeviceNameLength];
        var length = 0;
        for (var i = 0; i < DeviceNameLength; i++)
        {
            if (!TryReadRegister(bus, slaveAddress, (ushort)(DeviceName + i), out var value))
            {
                name = string.Empty;
                return false;
            }

            if (value is >= 0x20 and < 0x7F)
            {
                chars[length++] = (char)value;
            }
        }

        name = new string(chars[..length]).TrimEnd('\0', ' ', '\t', '\r', '\n');
        return true;
    }

    /// <summary>
    /// Reads the LED count from the config table. Values outside <c>1..30</c> (or a failed read) fall back to
    /// <see cref="MaxLeds"/> with <paramref name="isDetected"/> false.
    /// </summary>
    public static int ReadLedCount(IGpuI2cBus bus, byte slaveAddress, out bool isDetected)
    {
        if (TryReadRegister(bus, slaveAddress, ConfigTableLedCount, out var raw) && raw is >= 1 and <= MaxLeds)
        {
            isDetected = true;
            return raw;
        }

        isDetected = false;
        return MaxLeds;
    }

    /// <summary>Enables volatile direct mode (no save to the controller's flash).</summary>
    public static bool TryEnableDirectMode(IGpuI2cBus bus, byte slaveAddress) =>
        TryWriteRegister(bus, slaveAddress, DirectAccess, 0x01)
        && TryWriteRegister(bus, slaveAddress, Apply, 0x01);

    /// <summary>Leaves direct mode and restores the hardware rainbow effect. Best effort: every I2C result is ignored.</summary>
    public static void RestoreHardwareRainbow(IGpuI2cBus bus, byte slaveAddress)
    {
        _ = TryWriteRegister(bus, slaveAddress, DirectAccess, 0x00);

        // Speed 0x00 and mode 0x05 (rainbow, register 0x8021) through the speed/mode port staged at 0x8020.
        Span<byte> speedAndMode = [0x00, 0x05];
        _ = TrySelectRegister(bus, slaveAddress, DirectAccess);
        _ = bus.TryWriteRegisterBlock(slaveAddress, SpeedModePort, speedAndMode);

        // Apply pulse so firmware families that latch the mode only on 0x80A0 also leave direct mode.
        _ = TryWriteRegister(bus, slaveAddress, Apply, 0x01);

        _ = TryWriteRegister(bus, slaveAddress, DirectionRegister, 0x00);
        _ = TryWriteRegister(bus, slaveAddress, CommitRegister, 0x01);
    }

    /// <summary>Packs colors in R, B, G wire order. Missing colors are black. Returns the byte count.</summary>
    public static int PackFrame(ReadOnlySpan<RgbColor> colors, int ledCount, Span<byte> frame)
    {
        for (var i = 0; i < ledCount; i++)
        {
            var color = i < colors.Length ? colors[i] : default;
            var index = i * 3;
            frame[index] = color.R;
            frame[index + 1] = color.B;
            frame[index + 2] = color.G;
        }

        return ledCount * 3;
    }

    /// <summary>
    /// Streams <paramref name="frame"/> in blocks of up to 31 payload bytes, staging the color pointer
    /// <c>0x8100 + offset</c> before every block so firmware that does not auto-increment across separate block
    /// transactions still receives LEDs 11..30 at the right place. A single-chunk frame (offset 0) is unchanged.
    /// </summary>
    public static bool TryWriteFrame(IGpuI2cBus bus, byte slaveAddress, ReadOnlySpan<byte> frame, Span<byte> chunkPacket)
    {
        var offset = 0;
        while (offset < frame.Length)
        {
            if (!TrySelectRegister(bus, slaveAddress, (ushort)(ColorCtlV2 + offset)))
            {
                return false;
            }

            var chunkLength = Math.Min(frame.Length - offset, MaxChunkPayload);
            chunkPacket[0] = (byte)chunkLength;
            frame.Slice(offset, chunkLength).CopyTo(chunkPacket.Slice(1, chunkLength));
            if (!bus.TryWriteRegisterBlock(slaveAddress, BlockColorPort, chunkPacket[..(chunkLength + 1)]))
            {
                return false;
            }

            offset += chunkLength;
        }

        return true;
    }

    /// <summary>LED positions mirrored horizontally to match the physical wiring of ASUS GPUs.</summary>
    public static LedPosition[] CreateReversedLayout(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        if (count == 1)
        {
            return [new LedPosition(0.5f, 0.5f)];
        }

        var leds = new LedPosition[count];
        for (var i = 0; i < count; i++)
        {
            leds[i] = new LedPosition((count - 1 - i) / (float)(count - 1), 0.5f);
        }

        return leds;
    }
}
