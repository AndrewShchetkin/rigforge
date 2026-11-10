using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.Corsair;

/// <summary>
/// Corsair HID lighting packets (Lighting Node / Commander Pro and Commander Core).
/// Layouts follow OpenRGB's CorsairLightingNodeController and CorsairCommanderCoreController.
/// Every writer fills a caller-owned <see cref="Span{T}"/> and does not allocate.
/// Hardware-effect restore is volatile; nothing here writes a flash-persistence command.
/// </summary>
public static class CorsairProtocol
{
    public const int VendorId = 0x1B1C;

    public const ushort LightingNodeUsagePage = 0xFF00;
    public const int LightingNodeReportLength = 65;
    public const int LedsPerColorPacket = 50;
    public const int DefaultChannelLeds = 24;
    public const int MaxChannelLeds = 120;

    public const int LightingNodeProProductId = 0x0C0B;
    public const int CommanderProProductId = 0x0C10;
    public const int LightingNodeCoreProductId = 0x0C1A;
    public const int Ls100ProductId = 0x0C1E;

    public const ushort CommanderCoreUsagePage = 0xFF42;
    public const int CommanderCoreMinReportLength = 65;
    public const int CommanderCoreShortReportLength = 97;
    public const int CommanderCoreXtReportLength = 385;
    public const int CommanderCoreLongReportLength = 1025;
    public const int DefaultCommanderCoreLeds = 24;
    public const int MaxCommanderCoreLeds = 144;

    public const int CommanderCoreProductId = 0x0C1C;
    public const int CommanderCoreXtProductId = 0x0C2A;
    public const int IcueCommanderCoreProductId = 0x0C32;

    public const byte LightingNodeSoftwareMode = 0x02;
    public const byte LightingNodeHardwareMode = 0x01;

    public static readonly IReadOnlySet<int> LightingNodeProductIds = new HashSet<int>
    {
        LightingNodeProProductId,
        CommanderProProductId,
        LightingNodeCoreProductId,
        Ls100ProductId,
    };

    public static readonly IReadOnlySet<int> CommanderCoreProductIds = new HashSet<int>
    {
        CommanderCoreProductId,
        CommanderCoreXtProductId,
        IcueCommanderCoreProductId,
    };

    /// <summary>Node Core has one channel; Node Pro, Commander Pro and LS100 have two.</summary>
    public static int LightingNodeChannels(int productId) =>
        productId == LightingNodeCoreProductId ? 1 : 2;

    public static int LightingNodeColorPacketCount(int ledCount)
    {
        if (ledCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ledCount), ledCount, "LED count cannot be negative.");
        }

        if (ledCount == 0)
        {
            return 0;
        }

        var chunks = (ledCount + LedsPerColorPacket - 1) / LedsPerColorPacket;
        return chunks * 3;
    }

    /// <summary>Software lighting control: <c>[0x00, 0x38, channel, 0x02]</c>.</summary>
    public static void WriteLightingNodeSoftwareMode(byte channel, Span<byte> packet) =>
        WriteLightingNodeMode(channel, LightingNodeSoftwareMode, packet);

    /// <summary>Hardware playback restore on dispose: <c>[0x00, 0x38, channel, 0x01]</c>. Not a flash write.</summary>
    public static void WriteLightingNodeHardwareMode(byte channel, Span<byte> packet) =>
        WriteLightingNodeMode(channel, LightingNodeHardwareMode, packet);

    /// <summary>Begin a direct update: <c>[0x00, 0x34, channel]</c>.</summary>
    public static void WriteLightingNodeBegin(byte channel, Span<byte> packet)
    {
        RequireLightingNode(packet);
        packet.Clear();
        packet[0] = 0x00;
        packet[1] = 0x34;
        packet[2] = channel;
    }

    /// <summary>Commit the RGB planes: <c>[0x00, 0x33, 0xFF]</c>.</summary>
    public static void WriteLightingNodeCommit(Span<byte> packet)
    {
        RequireLightingNode(packet);
        packet.Clear();
        packet[0] = 0x00;
        packet[1] = 0x33;
        packet[2] = 0xFF;
    }

    /// <summary>
    /// One planar color packet: up to 50 LEDs of a single component.
    /// Packet order inside a chunk is red, green, then blue (component 0, 1, 2).
    /// </summary>
    public static void WriteLightingNodeColorPacket(byte channel, ReadOnlySpan<RgbColor> colors, int packetIndex, Span<byte> packet)
    {
        RequireLightingNode(packet);
        var packetCount = LightingNodeColorPacketCount(colors.Length);
        if (packetIndex < 0 || packetIndex >= packetCount)
        {
            throw new ArgumentOutOfRangeException(nameof(packetIndex), packetIndex, "Packet index is outside the frame.");
        }

        var chunk = packetIndex / 3;
        var component = packetIndex % 3;
        var start = chunk * LedsPerColorPacket;
        var count = Math.Min(LedsPerColorPacket, colors.Length - start);

        packet.Clear();
        packet[0] = 0x00;
        packet[1] = 0x32;
        packet[2] = channel;
        packet[3] = (byte)start;
        packet[4] = (byte)count;
        packet[5] = (byte)component;
        for (var i = 0; i < count; i++)
        {
            packet[6 + i] = Component(colors[start + i], component);
        }
    }

    /// <summary>Wake into software mode: <c>[0x00, 0x08, 0x01, 0x03, 0x00, 0x02]</c>.</summary>
    public static void WriteCommanderCoreSoftwareMode(Span<byte> packet)
    {
        RequireCommanderPrefix(packet);
        packet.Clear();
        packet[0] = 0x00;
        packet[1] = 0x08;
        packet[2] = 0x01;
        packet[3] = 0x03;
        packet[4] = 0x00;
        packet[5] = 0x02;
    }

    /// <summary>Return the controller to hardware playback: byte 5 is 0x01. Not a flash write.</summary>
    public static void WriteCommanderCoreHardwareMode(Span<byte> packet)
    {
        RequireCommanderPrefix(packet);
        packet.Clear();
        packet[0] = 0x00;
        packet[1] = 0x08;
        packet[2] = 0x01;
        packet[3] = 0x03;
        packet[4] = 0x00;
        packet[5] = 0x01;
    }

    /// <summary>Open the direct RGB endpoint: <c>[0x00, 0x08, 0x0D, 0x00, 0x22, 0x00]</c>.</summary>
    public static void WriteCommanderCoreOpenEndpoint(Span<byte> packet)
    {
        RequireCommanderPrefix(packet);
        packet.Clear();
        packet[0] = 0x00;
        packet[1] = 0x08;
        packet[2] = 0x0D;
        packet[3] = 0x00;
        packet[4] = 0x22;
        packet[5] = 0x00;
    }

    /// <summary>Close the endpoint after the RGB stream: <c>[0x00, 0x08, 0x05, 0x01]</c>.</summary>
    public static void WriteCommanderCoreCloseEndpoint(Span<byte> packet)
    {
        RequireCommanderPrefix(packet);
        packet.Clear();
        packet[0] = 0x00;
        packet[1] = 0x08;
        packet[2] = 0x05;
        packet[3] = 0x01;
    }

    /// <summary>RGB bytes that fit in the first <c>0x06</c> packet after its 10-byte header, rounded down to a triplet.</summary>
    public static int CommanderCoreFirstRgbCapacity(int reportLength)
    {
        var raw = reportLength - 10;
        if (raw < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(reportLength), reportLength, "A Commander Core report must fit a header and one LED.");
        }

        return raw - (raw % 3);
    }

    /// <summary>RGB bytes that fit in a <c>0x07</c> continuation packet, rounded down to a triplet.</summary>
    public static int CommanderCoreNextRgbCapacity(int reportLength)
    {
        var raw = reportLength - 4;
        if (raw < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(reportLength), reportLength, "A Commander Core continuation must fit one LED.");
        }

        return raw - (raw % 3);
    }

    public static int CommanderCoreRgbPacketCount(int ledCount, int reportLength)
    {
        if (ledCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ledCount), ledCount, "LED count cannot be negative.");
        }

        if (ledCount == 0)
        {
            return 0;
        }

        var rgbBytes = ledCount * 3;
        var first = CommanderCoreFirstRgbCapacity(reportLength);
        if (rgbBytes <= first)
        {
            return 1;
        }

        var next = CommanderCoreNextRgbCapacity(reportLength);
        var remaining = rgbBytes - first;
        return 1 + ((remaining + next - 1) / next);
    }

    /// <summary>
    /// One chunk of the direct RGB stream. The first packet is
    /// <c>[0x00, 0x08, 0x06, 0x00, len_lo, len_hi, 0x00, 0x00, 0x12, 0x00, R, G, B, ...]</c>
    /// where <c>len</c> is the full RGB payload length plus 2. Later packets use command <c>0x07</c>.
    /// </summary>
    public static void WriteCommanderCoreRgbPacket(ReadOnlySpan<RgbColor> colors, int packetIndex, int reportLength, Span<byte> packet)
    {
        if (reportLength < CommanderCoreMinReportLength)
        {
            throw new ArgumentOutOfRangeException(nameof(reportLength), reportLength, "Commander Core reports are at least 65 bytes.");
        }

        if (packet.Length < reportLength)
        {
            throw new ArgumentException($"A Commander Core packet buffer needs at least {reportLength} bytes.", nameof(packet));
        }

        var packetCount = CommanderCoreRgbPacketCount(colors.Length, reportLength);
        if (packetIndex < 0 || packetIndex >= packetCount)
        {
            throw new ArgumentOutOfRangeException(nameof(packetIndex), packetIndex, "Packet index is outside the frame.");
        }

        var rgbBytes = colors.Length * 3;
        var first = CommanderCoreFirstRgbCapacity(reportLength);
        packet.Clear();
        packet[0] = 0x00;
        packet[1] = 0x08;

        if (packetIndex == 0)
        {
            var realLength = rgbBytes + 2;
            packet[2] = 0x06;
            packet[3] = 0x00;
            packet[4] = (byte)(realLength & 0xFF);
            packet[5] = (byte)((realLength >> 8) & 0xFF);
            packet[6] = 0x00;
            packet[7] = 0x00;
            packet[8] = 0x12;
            packet[9] = 0x00;
            WriteInterleavedRgb(colors, 0, Math.Min(first, rgbBytes) / 3, packet[10..]);
            return;
        }

        var next = CommanderCoreNextRgbCapacity(reportLength);
        var startByte = first + ((packetIndex - 1) * next);
        var leds = Math.Min(next, rgbBytes - startByte) / 3;
        packet[2] = 0x07;
        packet[3] = 0x00;
        WriteInterleavedRgb(colors, startByte / 3, leds, packet[4..]);
    }

    private static void WriteLightingNodeMode(byte channel, byte mode, Span<byte> packet)
    {
        RequireLightingNode(packet);
        packet.Clear();
        packet[0] = 0x00;
        packet[1] = 0x38;
        packet[2] = channel;
        packet[3] = mode;
    }

    private static void WriteInterleavedRgb(ReadOnlySpan<RgbColor> colors, int startLed, int ledCount, Span<byte> destination)
    {
        for (var i = 0; i < ledCount; i++)
        {
            var color = colors[startLed + i];
            destination[i * 3] = color.R;
            destination[(i * 3) + 1] = color.G;
            destination[(i * 3) + 2] = color.B;
        }
    }

    private static byte Component(RgbColor color, int component) => component switch
    {
        0 => color.R,
        1 => color.G,
        _ => color.B,
    };

    private static void RequireLightingNode(Span<byte> packet)
    {
        if (packet.Length < LightingNodeReportLength)
        {
            throw new ArgumentException($"A Lighting Node packet buffer needs at least {LightingNodeReportLength} bytes.", nameof(packet));
        }
    }

    private static void RequireCommanderPrefix(Span<byte> packet)
    {
        if (packet.Length < 6)
        {
            throw new ArgumentException("A Commander Core command buffer needs at least 6 bytes.", nameof(packet));
        }
    }
}
