using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.Gpu.Msi;

internal enum MsiGpuGeneration
{
    Blackwell,
    Lovelace,
}

/// <summary>Model-specific LED wiring: <see cref="HardwareIndices"/>[i] is the controller slot driven by logical LED i.</summary>
internal sealed record MsiGpuLayout(MsiGpuGeneration Generation, string Model, int[] HardwareIndices, bool IsResizable)
{
    public int DefaultLedCount => IsResizable ? MsiMysticGpuProtocol.DefaultResizableLedCount : HardwareIndices.Length;

    public int MaxLedCount => IsResizable ? MsiMysticGpuProtocol.MaxHardwareLeds : HardwareIndices.Length;
}

/// <summary>
/// MSI Mystic Light GPU controller (<c>0x68</c>): Blackwell (RTX 50) streams 5-byte LED records to register
/// <c>0x04</c>, Lovelace (RTX 40) streams a fixed 136-byte BGR0 frame to register <c>0x49</c>. Software mode is
/// volatile; nothing is ever saved to the controller.
/// </summary>
internal static class MsiMysticGpuProtocol
{
    public const byte DefaultSlaveAddress = 0x68;
    public const int MaxHardwareLeds = 34;
    public const int DefaultResizableLedCount = 6;
    public const int LovelaceFrameLength = MaxHardwareLeds * 4;
    public const int BlackwellRecordLength = 5;
    public const int MaxFrameLength = MaxHardwareLeds * BlackwellRecordLength + 1;

    private const byte BlackwellFrameRegister = 0x04;
    private const byte LovelaceFrameRegister = 0x49;
    private const string DefaultVariant = "Blackwell:GAMING TRIO";

    private static readonly int[] VanguardMap = [0, 1, 2, 3, 4, 5, 6, 7, 11, 8, 9, 10, 12, 18, 21, 22, 15, 16, 17, 19, 20, 13, 14];

    /// <summary>Parses <c>"Generation:MODEL"</c> (default <c>Blackwell:GAMING TRIO</c>).</summary>
    public static MsiGpuLayout ParseVariant(string? variant)
    {
        var text = string.IsNullOrWhiteSpace(variant) ? DefaultVariant : variant.Trim();
        var separator = text.IndexOf(':', StringComparison.Ordinal);
        var generationText = separator < 0 ? text : text[..separator];
        var model = separator < 0 ? string.Empty : text[(separator + 1)..].Trim();
        var generation = generationText.Equals("Lovelace", StringComparison.OrdinalIgnoreCase)
            ? MsiGpuGeneration.Lovelace
            : MsiGpuGeneration.Blackwell;

        if (model.Equals("GAMING TRIO", StringComparison.OrdinalIgnoreCase))
        {
            return new MsiGpuLayout(generation, "GAMING TRIO", Identity(6), IsResizable: false);
        }

        if (model.Equals("SUPRIM SOC", StringComparison.OrdinalIgnoreCase) || model.Equals("SUPRIM", StringComparison.OrdinalIgnoreCase))
        {
            return new MsiGpuLayout(generation, "SUPRIM", Identity(15), IsResizable: false);
        }

        if (model.Equals("VANGUARD", StringComparison.OrdinalIgnoreCase))
        {
            return new MsiGpuLayout(generation, "VANGUARD", VanguardMap, IsResizable: false);
        }

        return new MsiGpuLayout(generation, string.IsNullOrEmpty(model) ? "Generic" : model, Identity(MaxHardwareLeds), IsResizable: true);
    }

    /// <summary>Switches the controller into volatile software control. Returns false on the first unacknowledged write.</summary>
    public static bool TryInitialize(IGpuI2cBus bus, byte slaveAddress, MsiGpuGeneration generation)
    {
        if (generation == MsiGpuGeneration.Blackwell)
        {
            return TryWriteByte(bus, slaveAddress, 0x46, 0x01)
                && TryWriteByte(bus, slaveAddress, 0x06, 0x00)
                && TryWriteByte(bus, slaveAddress, 0x20, 0x00);
        }

        return TryWriteByte(bus, slaveAddress, 0x2D, 0x00)
            && TryWriteByte(bus, slaveAddress, 0x46, 0x01)
            && TryWriteByte(bus, slaveAddress, 0x22, 0x1C)
            && TryWriteByte(bus, slaveAddress, 0x2E, 0x01)
            && TryWriteByte(bus, slaveAddress, 0x38, 0x04);
    }

    /// <summary>Length of the frame packet for the first <paramref name="ledCount"/> LEDs of <paramref name="layout"/>.</summary>
    public static int FrameLength(MsiGpuLayout layout, int ledCount)
    {
        if (layout.Generation == MsiGpuGeneration.Lovelace)
        {
            return LovelaceFrameLength;
        }

        var maxHardwareIndex = 0;
        for (var i = 0; i < ledCount; i++)
        {
            maxHardwareIndex = Math.Max(maxHardwareIndex, layout.HardwareIndices[i]);
        }

        return (maxHardwareIndex + 1) * BlackwellRecordLength + 1;
    }

    /// <summary>Builds the frame into <paramref name="packet"/> (zero-filled first) and returns its length. Missing colors are black.</summary>
    public static int BuildFrame(MsiGpuLayout layout, int ledCount, ReadOnlySpan<RgbColor> colors, int frameLength, Span<byte> packet)
    {
        packet[..frameLength].Clear();
        if (layout.Generation == MsiGpuGeneration.Lovelace)
        {
            for (var i = 0; i < ledCount; i++)
            {
                var color = i < colors.Length ? colors[i] : default;
                var offset = layout.HardwareIndices[i] * 4;
                packet[offset] = color.B;
                packet[offset + 1] = color.G;
                packet[offset + 2] = color.R;
            }

            return frameLength;
        }

        packet[0] = 0x01;
        for (var i = 0; i < ledCount; i++)
        {
            var color = i < colors.Length ? colors[i] : default;
            var hardwareIndex = layout.HardwareIndices[i];
            var offset = 1 + hardwareIndex * BlackwellRecordLength;
            packet[offset] = (byte)hardwareIndex;
            packet[offset + 1] = color.R;
            packet[offset + 2] = color.G;
            packet[offset + 3] = color.B;
            packet[offset + 4] = 0x01;
        }

        return frameLength;
    }

    public static bool TryWriteFrame(IGpuI2cBus bus, byte slaveAddress, MsiGpuGeneration generation, ReadOnlySpan<byte> packet)
    {
        var register = generation == MsiGpuGeneration.Lovelace ? LovelaceFrameRegister : BlackwellFrameRegister;
        return TryWriteBlock(bus, slaveAddress, register, packet);
    }

    private static bool TryWriteByte(IGpuI2cBus bus, byte slaveAddress, byte register, byte value)
    {
        ReadOnlySpan<byte> registerAddress = [register];
        ReadOnlySpan<byte> data = [value];
        return bus.TryWriteRegisterBlock(slaveAddress, registerAddress, data);
    }

    private static bool TryWriteBlock(IGpuI2cBus bus, byte slaveAddress, byte register, ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> registerAddress = [register];
        return bus.TryWriteRegisterBlock(slaveAddress, registerAddress, data);
    }

    private static int[] Identity(int count)
    {
        var map = new int[count];
        for (var i = 0; i < count; i++)
        {
            map[i] = i;
        }

        return map;
    }
}
