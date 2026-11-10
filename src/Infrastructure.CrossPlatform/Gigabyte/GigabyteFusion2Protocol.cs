using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.Gigabyte;

/// <summary>
/// Gigabyte RGB Fusion 2.0 / ITE 8297 packets (64 bytes, report id 0xCC).
/// Apply is the volatile <c>0x28 0xFF</c> execute. Flash saves <c>0x29</c> and <c>0x30</c> are never written.
/// </summary>
public static class GigabyteFusion2Protocol
{
    public const int VendorId = 0x048D;
    public const byte ReportId = 0xCC;
    public const int ReportLength = 64;
    public const int LedsPerPacket = 19;
    public const int DefaultAddressableLeds = 24;
    public const int MaxAddressableLeds = 120;

    public const byte DLed1 = 0x58;
    public const byte DLed2 = 0x59;
    public const byte OnboardHeader = 0x20;
    public const byte StaticEffect = 0x01;
    public const byte MaxBrightness = 0x5A;
    public const byte ApplyCommand = 0x28;
    public const byte ApplyArgument = 0xFF;

    public const int ProductIdIt8297 = 0x8297;
    public const int ProductId5702 = 0x5702;
    public const int ProductId5711 = 0x5711;

    public static readonly IReadOnlySet<int> ProductIds = new HashSet<int>
    {
        ProductIdIt8297,
        ProductId5702,
        ProductId5711,
    };

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

    /// <summary>
    /// Addressable header packet: <c>[0xCC, hdr, byteOffsetLo, byteOffsetHi, byteCount, G, R, B, ...]</c>.
    /// The offset is the byte index of the first LED (<c>ledIndex * 3</c>). Up to 19 LEDs per packet.
    /// </summary>
    public static void WriteAddressablePacket(byte header, ReadOnlySpan<RgbColor> colors, int packetIndex, Span<byte> packet)
    {
        Require(packet);
        var packetCount = PacketCount(colors.Length);
        if (packetIndex < 0 || packetIndex >= packetCount)
        {
            throw new ArgumentOutOfRangeException(nameof(packetIndex), packetIndex, "Packet index is outside the frame.");
        }

        var start = packetIndex * LedsPerPacket;
        var count = Math.Min(LedsPerPacket, colors.Length - start);
        var byteOffset = start * 3;

        packet.Clear();
        packet[0] = ReportId;
        packet[1] = header;
        packet[2] = (byte)(byteOffset & 0xFF);
        packet[3] = (byte)((byteOffset >> 8) & 0xFF);
        packet[4] = (byte)(count * 3);
        for (var i = 0; i < count; i++)
        {
            var color = colors[start + i];
            packet[5 + (i * 3)] = color.G;
            packet[6 + (i * 3)] = color.R;
            packet[7 + (i * 3)] = color.B;
        }
    }

    /// <summary>
    /// Onboard static/direct packet. <paramref name="zoneHeader"/> is the Fusion header index (0x20 = I/O).
    /// Effect type is 0x01 and max brightness is 0x5A.
    /// </summary>
    public static void WriteOnboardPacket(byte zoneHeader, RgbColor color, Span<byte> packet)
    {
        Require(packet);
        packet.Clear();
        packet[0] = ReportId;
        packet[1] = zoneHeader;
        packet[0x0B] = StaticEffect;
        packet[0x0C] = MaxBrightness;
        packet[0x0E] = color.R;
        packet[0x0F] = color.G;
        packet[0x10] = color.B;
        packet[0x1E] = 0x01;
    }

    /// <summary>Volatile execute: <c>[0xCC, 0x28, 0xFF]</c>. Never <c>0x29</c> or <c>0x30</c>.</summary>
    public static void WriteExecute(Span<byte> packet)
    {
        Require(packet);
        packet.Clear();
        packet[0] = ReportId;
        packet[1] = ApplyCommand;
        packet[2] = ApplyArgument;
    }

    private static void Require(Span<byte> packet)
    {
        if (packet.Length < ReportLength)
        {
            throw new ArgumentException($"A Fusion 2 packet buffer needs at least {ReportLength} bytes.", nameof(packet));
        }
    }
}
