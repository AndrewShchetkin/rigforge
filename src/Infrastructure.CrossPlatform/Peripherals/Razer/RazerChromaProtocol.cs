using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.Peripherals.Razer;

/// <summary>
/// Razer Chroma USB HID report. The 90-byte payload (91 with the leading report id) carries
/// status, transaction id, size, command class/id, 80 argument bytes, an XOR CRC and a reserved byte.
/// CRC is the XOR of payload bytes <c>[2..87]</c>, stored at payload <c>[88]</c> (buffer <c>[89]</c> when a report id is present).
/// Custom-frame apply is volatile. No packet stores a profile.
/// </summary>
public static class RazerChromaProtocol
{
    public const int VendorId = 0x1532;
    public const int PayloadLength = 90;
    public const int ReportLength = 91;
    public const byte ReportId = 0x00;
    public const byte TransactionId = 0x1F;
    public const byte TransactionIdAlternate = 0x3F;
    public const byte CommandClassExtendedMatrix = 0x0F;
    public const byte CommandSetEffect = 0x02;
    public const byte CommandCustomFrame = 0x03;
    public const byte EffectCustomFrame = 0x08;
    public const int ArgumentOffset = 8;
    public const int ArgumentCapacity = 80;
    public const int CrcIndex = 88;
    public const int MaxLedsPerFrame = (ArgumentCapacity - 3) / 3;

    public const int ArgbControllerProductId = 0x0F1F;
    public const int ArgbPortCount = 6;
    public const int DefaultArgbLedsPerPort = 20;
    public const int MaxArgbLedsPerPort = 80;

    public static readonly IReadOnlySet<int> ProductIds = new HashSet<int>
    {
        ArgbControllerProductId, // Chroma Addressable RGB Controller, 6 ports
        0x0084, // DeathAdder V2
        0x0078, // Viper Ultimate
        0x008A, // Basilisk V3
        0x024E, // BlackWidow V3
        0x0258, // Huntsman Mini
        0x0C00, // Firefly
    };

    /// <summary>XOR of <paramref name="report90"/> bytes <c>[2..87]</c>.</summary>
    public static byte ComputeCrc(ReadOnlySpan<byte> report90)
    {
        if (report90.Length < PayloadLength)
        {
            throw new ArgumentException($"CRC input must be the {PayloadLength}-byte Razer payload.", nameof(report90));
        }

        byte crc = 0;
        for (var i = 2; i <= 87; i++)
        {
            crc ^= report90[i];
        }

        return crc;
    }

    /// <summary>
    /// Chroma extended-matrix custom frame (class <c>0x0F</c>, id <c>0x03</c>).
    /// <paramref name="report"/> is either 90 bytes (payload) or 91 bytes (report id + payload).
    /// </summary>
    public static void WriteMatrixCustomFrame(byte row, byte startColumn, ReadOnlySpan<RgbColor> colors, Span<byte> report, byte transactionId = TransactionId)
    {
        if (colors.Length is < 1 or > MaxLedsPerFrame)
        {
            throw new ArgumentOutOfRangeException(nameof(colors), colors.Length, $"A custom frame carries 1..{MaxLedsPerFrame} LEDs.");
        }

        var payload = PayloadOf(report);
        payload.Clear();
        if (report.Length >= ReportLength)
        {
            report[0] = ReportId;
        }

        var dataSize = (byte)(3 + (colors.Length * 3));
        payload[1] = transactionId;
        payload[5] = dataSize;
        payload[6] = CommandClassExtendedMatrix;
        payload[7] = CommandCustomFrame;
        payload[ArgumentOffset] = row;
        payload[ArgumentOffset + 1] = startColumn;
        var stop = (byte)(startColumn + colors.Length - 1);
        payload[ArgumentOffset + 2] = stop;
        for (var i = 0; i < colors.Length; i++)
        {
            var offset = ArgumentOffset + 3 + (i * 3);
            payload[offset] = colors[i].R;
            payload[offset + 1] = colors[i].G;
            payload[offset + 2] = colors[i].B;
        }

        payload[CrcIndex] = ComputeCrc(payload);
    }

    /// <summary>
    /// Applies the staged custom frame (class <c>0x0F</c>, id <c>0x02</c>, effect <c>0x08</c>).
    /// This is the volatile "static/custom" commit. It does not write flash.
    /// </summary>
    public static void WriteStaticEffect(Span<byte> report, byte transactionId = TransactionId)
    {
        var payload = PayloadOf(report);
        payload.Clear();
        if (report.Length >= ReportLength)
        {
            report[0] = ReportId;
        }

        payload[1] = transactionId;
        payload[5] = 0x06;
        payload[6] = CommandClassExtendedMatrix;
        payload[7] = CommandSetEffect;
        payload[ArgumentOffset] = 0x00;
        payload[ArgumentOffset + 1] = 0x00;
        payload[ArgumentOffset + 2] = EffectCustomFrame;
        payload[CrcIndex] = ComputeCrc(payload);
    }

    public static bool IsMouse(int productId) =>
        productId is 0x0084 or 0x0078 or 0x008A;

    public static bool IsKeyboard(int productId) =>
        productId is 0x024E or 0x0258;

    private static Span<byte> PayloadOf(Span<byte> report)
    {
        if (report.Length >= ReportLength)
        {
            return report[1..ReportLength];
        }

        if (report.Length >= PayloadLength)
        {
            return report[..PayloadLength];
        }

        throw new ArgumentException($"A Razer report needs {PayloadLength} or {ReportLength} bytes.", nameof(report));
    }
}
