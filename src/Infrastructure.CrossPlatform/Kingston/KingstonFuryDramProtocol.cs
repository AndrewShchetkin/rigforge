using System.Text;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Smbus;

namespace RigForge.Infrastructure.CrossPlatform.Kingston;

public enum KingstonFuryModel : byte
{
    BeastDdr5 = 0x10,
    RenegadeDdr5 = 0x11,
    BeastWhiteDdr5 = 0x12,
    Beast2Ddr5 = 0x15,
}

public enum KingstonSlotProbeStatus
{
    Supported,
    NoDevice,
    SignatureMismatch,
    UnsupportedModel,
    IoError,
}

public sealed record KingstonSlotProbeResult(
    int SlotIndex,
    byte Address,
    KingstonSlotProbeStatus Status,
    string? Signature,
    byte? ModelCode,
    string Detail);

/// <summary>
/// A single register write across one or more DIMM slots, followed by a single 10 ms controller delay.
/// </summary>
public sealed record KingstonRegisterBatchWrite(
    byte Register,
    IReadOnlyList<(int SlotPosition, byte Address, byte Value)> Writes);

/// <summary>
/// Hardware firewall for SMBus access. Blocks any read or write outside the Kingston FURY DDR5 RGB
/// controller address range (<c>0x60..0x67</c>) and whitelist of volatile lighting registers, protecting
/// DDR5 SPD5118 (<c>0x50..0x57</c>), PMIC (<c>0x48..0x4F</c>) and DDR4 page addresses (<c>0x36..0x37</c>).
/// </summary>
public static class SmbusSafetyGuard
{
    public const byte MinAllowedAddress = KingstonFuryDramProtocol.BaseAddressDdr5;
    public const byte DefaultMaxAllowedAddress = KingstonFuryDramProtocol.BaseAddressDdr5 + 3; // 0x60..0x63
    public const byte HardMaxAllowedAddress = KingstonFuryDramProtocol.BaseAddressDdr5 + 7;    // 0x60..0x67

    public static void ValidateAddress(byte address, byte maxAllowedAddress = DefaultMaxAllowedAddress)
    {
        var upper = Math.Min(maxAllowedAddress, HardMaxAllowedAddress);
        if (address < MinAllowedAddress || address > upper)
        {
            throw new ArgumentOutOfRangeException(
                nameof(address),
                $"0x{address:X2}",
                $"SMBus safety guard blocked address 0x{address:X2}. Only Kingston FURY DDR5 RGB addresses 0x{MinAllowedAddress:X2}..0x{upper:X2} are permitted (SPD 0x50..0x57 and PMIC 0x48..0x4F are strictly forbidden).");
        }
    }

    public static void ValidateRead(byte address, byte register, byte maxAllowedAddress = DefaultMaxAllowedAddress)
    {
        ValidateAddress(address, maxAllowedAddress);

        var allowed = register is >= KingstonFuryDramProtocol.RegisterSignatureStart and <= KingstonFuryDramProtocol.RegisterSignatureEnd
            or KingstonFuryDramProtocol.RegisterModel
            or KingstonFuryDramProtocol.RegisterMode;

        if (!allowed)
        {
            throw new ArgumentOutOfRangeException(
                nameof(register),
                $"0x{register:X2}",
                $"SMBus safety guard blocked reading register 0x{register:X2} on 0x{address:X2}. Only signature (0x01..0x04), model (0x06) and mode (0x09) registers may be read.");
        }
    }

    public static void ValidateWrite(byte address, byte register, byte value, byte maxAllowedAddress = DefaultMaxAllowedAddress)
    {
        ValidateAddress(address, maxAllowedAddress);

        switch (register)
        {
            case KingstonFuryDramProtocol.RegisterApply:
                if (value is not (KingstonFuryDramProtocol.TransactionBegin or KingstonFuryDramProtocol.TransactionEnd))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(value),
                        $"0x{value:X2}",
                        $"SMBus safety guard blocked writing 0x{value:X2} to apply register 0x08. Only 0x53 (Begin) and 0x44 (End) are permitted.");
                }

                return;

            case KingstonFuryDramProtocol.RegisterMode:
                if (value != KingstonFuryDramProtocol.ModeDirect)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(value),
                        $"0x{value:X2}",
                        $"SMBus safety guard blocked writing mode 0x{value:X2} to register 0x09. Only Direct Mode (0x10) is permitted.");
                }

                return;

            case KingstonFuryDramProtocol.RegisterIndex:
                if (value > 3)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(value),
                        $"0x{value:X2}",
                        "SMBus safety guard blocked writing slot index > 3 to register 0x0B.");
                }

                return;

            case KingstonFuryDramProtocol.RegisterBrightness:
                if (value > KingstonFuryDramProtocol.MaxBrightness)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(value),
                        value,
                        "SMBus safety guard blocked writing brightness > 100 to register 0x20.");
                }

                return;

            case KingstonFuryDramProtocol.RegisterNumSlots:
                if (value is < 1 or > 4)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(value),
                        value,
                        "SMBus safety guard blocked writing slot count outside 1..4 to register 0x27.");
                }

                return;

            case >= KingstonFuryDramProtocol.RegisterBaseRed and <= KingstonFuryDramProtocol.RegisterLastLedByte:
                return;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(register),
                    $"0x{register:X2}",
                    $"SMBus safety guard blocked writing to register 0x{register:X2} on 0x{address:X2}.");
        }
    }
}

/// <summary>
/// Pure protocol builders, register cache diffing and signature verification for Kingston FURY DDR5 DRAM RGB,
/// following OpenRGB's <c>KingstonFuryDRAMController</c>.
/// </summary>
public static class KingstonFuryDramProtocol
{
    public const byte BaseAddressDdr5 = 0x60;
    public const int LedsPerDimmDdr5 = 12;
    public const int MaxSupportedSlots = 8;

    public const byte RegisterSignatureStart = 0x01;
    public const byte RegisterSignatureEnd = 0x04;
    public const byte RegisterModel = 0x06;
    public const byte RegisterApply = 0x08;
    public const byte RegisterMode = 0x09;
    public const byte RegisterIndex = 0x0B;
    public const byte RegisterBrightness = 0x20;
    public const byte RegisterNumSlots = 0x27;
    public const byte RegisterBaseRed = 0x50;
    public const byte RegisterBaseGreen = 0x51;
    public const byte RegisterBaseBlue = 0x52;
    public const byte RegisterLastLedByte = RegisterBaseRed + (LedsPerDimmDdr5 * 3) - 1; // 0x73

    public const byte TransactionBegin = 0x53;
    public const byte TransactionEnd = 0x44;
    public const byte ModeDirect = 0x10;
    public const byte MaxBrightness = 100;

    public static readonly TimeSpan ControllerDelay = TimeSpan.FromMilliseconds(10);
    public static readonly TimeSpan TransactionDelay = TimeSpan.FromMilliseconds(3);
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(3);

    private static ReadOnlySpan<byte> ExpectedSignature => "FURY"u8;

    public static byte GetSlotAddress(int slotIndex)
    {
        if (slotIndex is < 0 or >= MaxSupportedSlots)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "DIMM slot index must be in 0..7.");
        }

        return (byte)(BaseAddressDdr5 + slotIndex);
    }

    public static bool IsSupportedDdr5Model(byte modelCode) =>
        modelCode is (byte)KingstonFuryModel.BeastDdr5
            or (byte)KingstonFuryModel.RenegadeDdr5
            or (byte)KingstonFuryModel.BeastWhiteDdr5
            or (byte)KingstonFuryModel.Beast2Ddr5;

    public static string DescribeModel(byte modelCode) => modelCode switch
    {
        (byte)KingstonFuryModel.BeastDdr5 => "Kingston FURY Beast DDR5 (0x10)",
        (byte)KingstonFuryModel.RenegadeDdr5 => "Kingston FURY Renegade DDR5 (0x11)",
        (byte)KingstonFuryModel.BeastWhiteDdr5 => "Kingston FURY Beast RGB White DDR5 (0x12)",
        (byte)KingstonFuryModel.Beast2Ddr5 => "Kingston FURY Beast DDR5 rev.2 (0x15)",
        _ => $"Unknown (0x{modelCode:X2})",
    };

    /// <summary>
    /// Extracts the payload byte from an SMBus word read. Kingston FURY controllers return the register value
    /// in the high byte (<c>(word &gt;&gt; 8) &amp; 0xFF</c>).
    /// </summary>
    public static byte ExtractWordPayload(ushort word) => (byte)((word >> 8) & 0xFF);

    /// <summary>
    /// Probes a single slot address (<c>0x60 + slotIndex</c>) by opening a transfer transaction, reading the
    /// 4-byte <c>"FURY"</c> ASCII signature from registers <c>0x01..0x04</c>, reading the model byte at <c>0x06</c>,
    /// and always closing the transaction with <c>0x08 = 0x44</c>.
    /// </summary>
    public static KingstonSlotProbeResult ProbeSlot(
        ISmbusController bus,
        int slotIndex,
        Action<TimeSpan>? delay = null,
        byte maxAllowedAddress = SmbusSafetyGuard.DefaultMaxAllowedAddress)
    {
        var wait = delay ?? Thread.Sleep;
        var address = GetSlotAddress(slotIndex);
        SmbusSafetyGuard.ValidateAddress(address, maxAllowedAddress);

        SmbusSafetyGuard.ValidateWrite(address, RegisterApply, TransactionBegin, maxAllowedAddress);
        if (!bus.TryWriteByteData(address, RegisterApply, TransactionBegin))
        {
            return new KingstonSlotProbeResult(
                slotIndex,
                address,
                KingstonSlotProbeStatus.NoDevice,
                Signature: null,
                ModelCode: null,
                Detail: $"No response at 0x{address:X2}");
        }

        wait(ControllerDelay);

        try
        {
            Span<byte> sigBytes = stackalloc byte[4];
            for (var i = 0; i < 4; i++)
            {
                var reg = (byte)(RegisterSignatureStart + i);
                if (!TryReadRegisterWithRetry(bus, address, reg, wait, maxAllowedAddress, out var value))
                {
                    return new KingstonSlotProbeResult(
                        slotIndex,
                        address,
                        KingstonSlotProbeStatus.IoError,
                        Signature: null,
                        ModelCode: null,
                        Detail: $"Failed to read signature register 0x{reg:X2} at 0x{address:X2}");
                }

                sigBytes[i] = value;
            }

            var signature = Encoding.ASCII.GetString(sigBytes);
            if (!sigBytes.SequenceEqual(ExpectedSignature))
            {
                return new KingstonSlotProbeResult(
                    slotIndex,
                    address,
                    KingstonSlotProbeStatus.SignatureMismatch,
                    signature,
                    ModelCode: null,
                    Detail: $"Signature mismatch at 0x{address:X2}: expected 'FURY', got '{signature}'");
            }

            if (!TryReadRegisterWithRetry(bus, address, RegisterModel, wait, maxAllowedAddress, out var modelCode))
            {
                return new KingstonSlotProbeResult(
                    slotIndex,
                    address,
                    KingstonSlotProbeStatus.IoError,
                    signature,
                    ModelCode: null,
                    Detail: $"Failed to read model register 0x06 at 0x{address:X2}");
            }

            if (!IsSupportedDdr5Model(modelCode))
            {
                return new KingstonSlotProbeResult(
                    slotIndex,
                    address,
                    KingstonSlotProbeStatus.UnsupportedModel,
                    signature,
                    modelCode,
                    Detail: $"Unsupported Kingston FURY model code 0x{modelCode:X2} at 0x{address:X2}");
            }

            return new KingstonSlotProbeResult(
                slotIndex,
                address,
                KingstonSlotProbeStatus.Supported,
                signature,
                modelCode,
                Detail: DescribeModel(modelCode));
        }
        finally
        {
            SmbusSafetyGuard.ValidateWrite(address, RegisterApply, TransactionEnd, maxAllowedAddress);
            _ = bus.TryWriteByteData(address, RegisterApply, TransactionEnd);
            wait(ControllerDelay);
        }
    }

    /// <summary>
    /// Computes the minimal sequence of SMBus register writes required to transition from the cached register
    /// state to <paramref name="colors"/>. Unchanged registers are omitted so static frames produce zero writes.
    /// </summary>
    public static IReadOnlyList<KingstonRegisterBatchWrite> BuildFrameWritePlan(
        IReadOnlyList<byte> slotAddresses,
        ReadOnlySpan<RgbColor> colors,
        byte brightness,
        byte?[,] registerCache)
    {
        ArgumentNullException.ThrowIfNull(slotAddresses);
        ArgumentNullException.ThrowIfNull(registerCache);

        var slotCount = slotAddresses.Count;
        if (slotCount == 0)
        {
            return [];
        }

        var expectedLeds = slotCount * LedsPerDimmDdr5;
        if (colors.Length < expectedLeds)
        {
            throw new ArgumentException($"Expected at least {expectedLeds} LED colors for {slotCount} DIMM(s), got {colors.Length}.", nameof(colors));
        }

        var clampedBrightness = Math.Clamp(brightness, (byte)0, MaxBrightness);
        var numSlotsValue = (byte)Math.Clamp(slotCount, 1, 4);
        var batches = new List<KingstonRegisterBatchWrite>(40);

        AddUniformRegisterIfChanged(batches, slotAddresses, registerCache, RegisterMode, ModeDirect);

        for (var ledIndex = 0; ledIndex < LedsPerDimmDdr5; ledIndex++)
        {
            var redReg = (byte)(RegisterBaseRed + (3 * ledIndex));
            var greenReg = (byte)(RegisterBaseGreen + (3 * ledIndex));
            var blueReg = (byte)(RegisterBaseBlue + (3 * ledIndex));

            // Follow OpenRGB's KingstonFuryDRAMController::SetLEDColors order: red, blue, green.
            AddPerSlotRegisterIfChanged(batches, slotAddresses, registerCache, redReg, colors, ledIndex, static c => c.R);
            AddPerSlotRegisterIfChanged(batches, slotAddresses, registerCache, blueReg, colors, ledIndex, static c => c.B);
            AddPerSlotRegisterIfChanged(batches, slotAddresses, registerCache, greenReg, colors, ledIndex, static c => c.G);
        }

        AddUniformRegisterIfChanged(batches, slotAddresses, registerCache, RegisterBrightness, clampedBrightness);
        AddUniformRegisterIfChanged(batches, slotAddresses, registerCache, RegisterNumSlots, numSlotsValue);

        return batches;
    }

    private static void AddUniformRegisterIfChanged(
        List<KingstonRegisterBatchWrite> batches,
        IReadOnlyList<byte> slotAddresses,
        byte?[,] registerCache,
        byte register,
        byte value)
    {
        List<(int SlotPosition, byte Address, byte Value)>? writes = null;
        for (var slotPos = 0; slotPos < slotAddresses.Count; slotPos++)
        {
            if (registerCache[slotPos, register] == value)
            {
                continue;
            }

            writes ??= new List<(int, byte, byte)>(slotAddresses.Count);
            writes.Add((slotPos, slotAddresses[slotPos], value));
        }

        if (writes is { Count: > 0 })
        {
            batches.Add(new KingstonRegisterBatchWrite(register, writes));
        }
    }

    private static void AddPerSlotRegisterIfChanged(
        List<KingstonRegisterBatchWrite> batches,
        IReadOnlyList<byte> slotAddresses,
        byte?[,] registerCache,
        byte register,
        ReadOnlySpan<RgbColor> colors,
        int ledIndex,
        Func<RgbColor, byte> selector)
    {
        List<(int SlotPosition, byte Address, byte Value)>? writes = null;
        for (var slotPos = 0; slotPos < slotAddresses.Count; slotPos++)
        {
            var color = colors[(slotPos * LedsPerDimmDdr5) + ledIndex];
            var value = selector(color);
            if (registerCache[slotPos, register] == value)
            {
                continue;
            }

            writes ??= new List<(int, byte, byte)>(slotAddresses.Count);
            writes.Add((slotPos, slotAddresses[slotPos], value));
        }

        if (writes is { Count: > 0 })
        {
            batches.Add(new KingstonRegisterBatchWrite(register, writes));
        }
    }

    private static bool TryReadRegisterWithRetry(
        ISmbusController bus,
        byte address,
        byte register,
        Action<TimeSpan> wait,
        byte maxAllowedAddress,
        out byte value)
    {
        SmbusSafetyGuard.ValidateRead(address, register, maxAllowedAddress);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var ok = bus.TryReadWordData(address, register, out var word);
            wait(ControllerDelay);
            if (ok && word != 0xFFFF)
            {
                value = ExtractWordPayload(word);
                return true;
            }

            if (attempt < 3)
            {
                wait(TimeSpan.FromMilliseconds(30 * attempt));
            }
        }

        value = 0;
        return false;
    }
}
