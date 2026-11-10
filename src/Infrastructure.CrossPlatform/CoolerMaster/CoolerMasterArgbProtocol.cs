using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.CoolerMaster;

/// <summary>
/// Cooler Master ARGB Gen2 / Small ARGB controller packets (65 bytes).
/// Direct software mode starts with <c>[0x00, 0x80, ...]</c>. LED chunks follow the small-controller
/// direct layout (command 0x10). No packet persists the effect to flash.
/// </summary>
public static class CoolerMasterArgbProtocol
{
    public const int VendorId = 0x2516;
    public const int ReportLength = 65;
    public const int ColorHeaderLength = 6;
    public const int LedsPerPacket = 19;
    public const int PortCount = 3;
    public const int DefaultLedsPerPort = 24;
    public const int MaxLedsPerPort = 80;

    public const int SmallControllerProductId = 0x1011;
    public const int Gen2ControllerProductId = 0x0173;

    public const byte DirectReport = 0x80;
    public const byte DirectCommand = 0x10;
    public const byte LastPacketFlag = 0x80;

    public static readonly IReadOnlySet<int> ProductIds = new HashSet<int>
    {
        SmallControllerProductId,
        Gen2ControllerProductId,
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

    /// <summary>Direct software mode: <c>[0x00, 0x80, 0x10, 0x01, port, 0x00]</c>. Byte 5 stays 0 (no save).</summary>
    public static void WriteDirectMode(byte port, Span<byte> packet)
    {
        Require(packet);
        packet.Clear();
        packet[0] = 0x00;
        packet[1] = DirectReport;
        packet[2] = DirectCommand;
        packet[3] = 0x01;
        packet[4] = port;
        packet[5] = 0x00;
    }

    /// <summary>
    /// One RGB chunk: <c>[0x00, packetIndex, 0x10, 0x02, port, count, R, G, B, ...]</c>.
    /// The last packet sets bit 7 of the packet index, matching the small ARGB controller.
    /// </summary>
    public static void WriteColorPacket(byte port, int packetIndex, ReadOnlySpan<RgbColor> colors, Span<byte> packet)
    {
        Require(packet);
        var packetCount = PacketCount(colors.Length);
        if (packetIndex < 0 || packetIndex >= packetCount)
        {
            throw new ArgumentOutOfRangeException(nameof(packetIndex), packetIndex, "Packet index is outside the frame.");
        }

        var start = packetIndex * LedsPerPacket;
        var count = Math.Min(LedsPerPacket, colors.Length - start);
        packet.Clear();
        packet[0] = 0x00;
        packet[1] = (byte)packetIndex;
        if (packetIndex == packetCount - 1)
        {
            packet[1] |= LastPacketFlag;
        }

        packet[2] = DirectCommand;
        packet[3] = 0x02;
        packet[4] = port;
        packet[5] = (byte)count;
        for (var i = 0; i < count; i++)
        {
            var color = colors[start + i];
            packet[ColorHeaderLength + (i * 3)] = color.R;
            packet[ColorHeaderLength + (i * 3) + 1] = color.G;
            packet[ColorHeaderLength + (i * 3) + 2] = color.B;
        }
    }

    private static void Require(Span<byte> packet)
    {
        if (packet.Length < ReportLength)
        {
            throw new ArgumentException($"A Cooler Master packet buffer needs at least {ReportLength} bytes.", nameof(packet));
        }
    }
}
