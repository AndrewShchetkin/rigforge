using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.LianLi;

public enum LianLiColorOrder
{
    /// <summary>R, G, B on the wire.</summary>
    Rgb,

    /// <summary>R, B, G on the wire (SL-Infinity and AL hubs).</summary>
    Rbg,
}

/// <summary>
/// Lian Li UNI HUB frames. Report id is 0xE0. The direct header is <c>0xE0 0x10 0x60 &lt;channel&gt;</c>.
/// Color frames use command <c>0x30 + channel</c> and pack RGB or RBG triplets. Nothing here saves to flash.
/// </summary>
public static class LianLiUniHubProtocol
{
    public const int VendorId = 0x0CF2;
    public const byte ReportId = 0xE0;
    public const int ReportLength = 65;
    public const int ColorHeaderLength = 4;
    public const int ChannelCount = 4;
    public const int LedsPerFan = 16;
    public const int DefaultFansPerChannel = 3;
    public const int DefaultLedsPerChannel = LedsPerFan * DefaultFansPerChannel;
    public const int MaxLedsLegacy = 64;
    public const int MaxLedsV2 = 120;

    public const int UniHubSl = 0xA100;
    public const int UniHubAl = 0xA101;
    public const int UniHubSlInfinity = 0xA102;
    public const int UniHubSlV2 = 0xA103;
    public const int UniHubAlV2 = 0xA104;
    public const int StrimerPlusV2 = 0xA105;

    public static readonly IReadOnlySet<int> ProductIds = new HashSet<int>
    {
        UniHubSl,
        UniHubAl,
        UniHubSlInfinity,
        UniHubSlV2,
        UniHubAlV2,
        StrimerPlusV2,
    };

    public static int MaxLedsPerChannel(int productId) => productId switch
    {
        UniHubSlV2 or UniHubAlV2 or StrimerPlusV2 or UniHubSlInfinity => MaxLedsV2,
        _ => MaxLedsLegacy,
    };

    public static LianLiColorOrder ColorOrder(int productId) => productId switch
    {
        UniHubAl or UniHubAlV2 or UniHubSlInfinity => LianLiColorOrder.Rbg,
        _ => LianLiColorOrder.Rgb,
    };

    public static int LedsPerPacket(int reportLength)
    {
        var raw = reportLength - ColorHeaderLength;
        if (raw < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(reportLength), reportLength, "A UNI HUB color report must fit one LED.");
        }

        return raw / 3;
    }

    public static int ColorPacketCount(int ledCount, int reportLength)
    {
        if (ledCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ledCount), ledCount, "LED count cannot be negative.");
        }

        if (ledCount == 0)
        {
            return 0;
        }

        var perPacket = LedsPerPacket(reportLength);
        return (ledCount + perPacket - 1) / perPacket;
    }

    /// <summary>Direct-action header: <c>[0xE0, 0x10, 0x60, channel, ledCount, ...]</c>.</summary>
    public static void WriteModeHeader(byte channel, int ledCount, Span<byte> packet)
    {
        Require(packet);
        packet.Clear();
        packet[0] = ReportId;
        packet[1] = 0x10;
        packet[2] = 0x60;
        packet[3] = channel;
        packet[4] = (byte)Math.Clamp(ledCount, 0, 255);
    }

    /// <summary>
    /// One color frame: <c>[0xE0, 0x30 + channel, offset, count, triplets]</c>.
    /// <paramref name="order"/> selects RGB or RBG.
    /// </summary>
    public static void WriteColorPacket(byte channel, int packetIndex, ReadOnlySpan<RgbColor> colors, LianLiColorOrder order, int reportLength, Span<byte> packet)
    {
        if (packet.Length < reportLength)
        {
            throw new ArgumentException($"A UNI HUB color buffer needs at least {reportLength} bytes.", nameof(packet));
        }

        var packetCount = ColorPacketCount(colors.Length, reportLength);
        if (packetIndex < 0 || packetIndex >= packetCount)
        {
            throw new ArgumentOutOfRangeException(nameof(packetIndex), packetIndex, "Packet index is outside the frame.");
        }

        var perPacket = LedsPerPacket(reportLength);
        var start = packetIndex * perPacket;
        var count = Math.Min(perPacket, colors.Length - start);
        packet.Clear();
        packet[0] = ReportId;
        packet[1] = (byte)(0x30 + channel);
        packet[2] = (byte)start;
        packet[3] = (byte)count;
        for (var i = 0; i < count; i++)
        {
            var color = colors[start + i];
            var at = ColorHeaderLength + (i * 3);
            if (order == LianLiColorOrder.Rbg)
            {
                packet[at] = color.R;
                packet[at + 1] = color.B;
                packet[at + 2] = color.G;
            }
            else
            {
                packet[at] = color.R;
                packet[at + 1] = color.G;
                packet[at + 2] = color.B;
            }
        }
    }

    /// <summary>Volatile static apply for one channel. Brightness byte 0x00 is full brightness, not a save.</summary>
    public static void WriteStaticApply(byte channel, Span<byte> packet)
    {
        Require(packet);
        packet.Clear();
        packet[0] = ReportId;
        packet[1] = (byte)(0x10 + channel);
        packet[2] = 0x01;
        packet[3] = 0x00;
        packet[4] = 0x00;
        packet[5] = 0x00;
    }

    private static void Require(Span<byte> packet)
    {
        if (packet.Length < ReportLength)
        {
            throw new ArgumentException($"A UNI HUB packet buffer needs at least {ReportLength} bytes.", nameof(packet));
        }
    }
}
