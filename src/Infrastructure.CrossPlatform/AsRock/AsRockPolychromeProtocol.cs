using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.AsRock;

/// <summary>
/// ASRock Polychrome USB direct packets (65-byte HID reports, report id 0x00).
/// <c>0x10</c> addresses the onboard zone and <c>0x12</c> is the addressable RGB stream
/// (channel, LED offset, count, RGB triplets). Both always carry a color payload.
/// OpenRGB's empty <c>0x12</c> commit persists configuration across power cycles and is never sent.
/// </summary>
public static class AsRockPolychromeProtocol
{
    public const int VendorId = 0x26CE;
    public const int ProductId = 0x01A2;
    public const int ReportLength = 65;
    public const int HeaderLength = 5;
    public const int LedsPerPacket = 20;
    public const int DefaultArgbLeds = 24;
    public const int MaxArgbLeds = 100;

    public const byte OnboardHeader = 0x10;
    public const byte AddressableHeader = 0x12;
    public const byte OnboardChannel = 0x00;
    public const byte ArgbChannel = 0x01;

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

    /// <summary>Onboard direct color: <c>[0x00, 0x10, channel, offset, count, R, G, B]</c>.</summary>
    public static void WriteOnboardPacket(byte channel, RgbColor color, Span<byte> packet)
    {
        Require(packet);
        packet.Clear();
        packet[0] = 0x00;
        packet[1] = OnboardHeader;
        packet[2] = channel;
        packet[3] = 0x00;
        packet[4] = 0x01;
        packet[5] = color.R;
        packet[6] = color.G;
        packet[7] = color.B;
    }

    /// <summary>
    /// Addressable direct frame: <c>[0x00, 0x12, channel, offset, count, R, G, B, ...]</c>.
    /// The count byte is never zero, so this is not an empty persistence commit.
    /// </summary>
    public static void WriteAddressablePacket(byte channel, int packetIndex, ReadOnlySpan<RgbColor> colors, Span<byte> packet)
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
        packet[1] = AddressableHeader;
        packet[2] = channel;
        packet[3] = (byte)start;
        packet[4] = (byte)count;
        for (var i = 0; i < count; i++)
        {
            var color = colors[start + i];
            packet[HeaderLength + (i * 3)] = color.R;
            packet[HeaderLength + (i * 3) + 1] = color.G;
            packet[HeaderLength + (i * 3) + 2] = color.B;
        }
    }

    private static void Require(Span<byte> packet)
    {
        if (packet.Length < ReportLength)
        {
            throw new ArgumentException($"A Polychrome packet buffer needs at least {ReportLength} bytes.", nameof(packet));
        }
    }
}
