using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.Msi;

/// <summary>
/// MSI Mystic Light USB packets. The 185-byte feature report follows OpenRGB's FeaturePacket_185
/// (report id 0x52). <c>save_data</c> at offset 184 is always 0 — this driver never persists to flash.
/// </summary>
public static class MsiMysticLightProtocol
{
    public const int VendorId = 0x1462;
    public const int AlternateVendorId = 0x0DB0;

    public const byte ReportId = 0x52;
    public const int ReportLength = 185;
    public const int SaveOffset = 184;
    public const byte SteadyEffect = 0x01;

    /// <summary>Brightness 100% in the low speed/brightness flags field (<c>10 &lt;&lt; 2</c>).</summary>
    public const byte FullBrightnessFlags = 0x28;

    /// <summary>Bit 7 marks the zone color as active. This is not the flash-save flag.</summary>
    public const byte ColorEnabledFlag = 0x80;

    public const int ZoneSize = 10;
    public const int Jrgb1Offset = 1;
    public const int Jpipe1Offset = 11;
    public const int Jpipe2Offset = 21;
    public const int Jrainbow1Offset = 31;
    public const int Jrainbow2Offset = 42;
    public const int OnboardOffset = 74;
    public const int OnboardSlots = 10;
    public const int Jrgb2Offset = 174;

    public const byte ZoneJrgb1 = 0x01;
    public const byte ZoneJrgb2 = 0x02;
    public const byte ZoneJrainbow1 = 0x05;
    public const byte ZoneJrainbow2 = 0x06;
    public const byte ZoneOnboard = 0x0A;

    public const int StreamReportLength = 64;
    public const int StreamHeaderLength = 8;
    public const int StreamLedsPerPacket = 18;
    public const int DefaultArgbLeds = 24;
    public const int MaxArgbLeds = 120;

    public static readonly IReadOnlySet<int> VendorIds = new HashSet<int> { VendorId, AlternateVendorId };

    public static readonly IReadOnlySet<int> ProductIds = new HashSet<int>
    {
        0x7C75, 0x7C84, 0x7C90, 0x7C91, 0x7C94, 0x7C95,
        0x7D06, 0x7D09, 0x7D25, 0x7D32, 0x7D70, 0x7D75, 0x7D77, 0x7D78, 0x7D96,
        0x7E06, 0x7E12, 0x7E16, 0x7E26,
        0x0076,
    };

    public static int StreamPacketCount(int ledCount)
    {
        if (ledCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ledCount), ledCount, "LED count cannot be negative.");
        }

        if (ledCount == 0)
        {
            return 0;
        }

        return (ledCount + StreamLedsPerPacket - 1) / StreamLedsPerPacket;
    }

    /// <summary>
    /// Writes the 185-byte zone report.
    /// <paramref name="onboardAndHeaders"/> is Onboard, JRGB1, JRGB2, JRAINBOW1, JRAINBOW2.
    /// A single onboard color is copied to every onboard slot. Colors after the fifth replace onboard slots 1..9.
    /// Missing colors leave that zone disabled. Offset 184 stays 0.
    /// </summary>
    public static void Write185Report(ReadOnlySpan<RgbColor> onboardAndHeaders, Span<byte> report)
    {
        if (report.Length < ReportLength)
        {
            throw new ArgumentException($"A Mystic Light report needs at least {ReportLength} bytes.", nameof(report));
        }

        report.Clear();
        report[0] = ReportId;

        if (!onboardAndHeaders.IsEmpty)
        {
            var onboard = onboardAndHeaders[0];
            for (var slot = 0; slot < OnboardSlots; slot++)
            {
                WriteSteadyZone(report.Slice(OnboardOffset + (slot * ZoneSize), ZoneSize), onboard);
            }

            var extras = Math.Min(OnboardSlots - 1, onboardAndHeaders.Length - 5);
            for (var extra = 0; extra < extras; extra++)
            {
                WriteSteadyZone(report.Slice(OnboardOffset + ((extra + 1) * ZoneSize), ZoneSize), onboardAndHeaders[5 + extra]);
            }
        }

        if (onboardAndHeaders.Length > 1)
        {
            WriteSteadyZone(report.Slice(Jrgb1Offset, ZoneSize), onboardAndHeaders[1]);
        }

        if (onboardAndHeaders.Length > 2)
        {
            WriteSteadyZone(report.Slice(Jrgb2Offset, ZoneSize), onboardAndHeaders[2]);
        }

        if (onboardAndHeaders.Length > 3)
        {
            WriteSteadyZone(report.Slice(Jrainbow1Offset, ZoneSize), onboardAndHeaders[3]);
        }

        if (onboardAndHeaders.Length > 4)
        {
            WriteSteadyZone(report.Slice(Jrainbow2Offset, ZoneSize), onboardAndHeaders[4]);
        }

        report[SaveOffset] = 0x00;
    }

    /// <summary>
    /// Per-LED ARGB stream for 64/65-byte Mystic Light interfaces.
    /// Byte 6 is the save flag and is always 0.
    /// </summary>
    public static void WriteArgbStream(byte zone, int packetIndex, ReadOnlySpan<RgbColor> colors, Span<byte> packet)
    {
        if (packet.Length < StreamReportLength)
        {
            throw new ArgumentException($"An ARGB stream packet needs at least {StreamReportLength} bytes.", nameof(packet));
        }

        var packetCount = StreamPacketCount(colors.Length);
        if (packetIndex < 0 || packetIndex >= packetCount)
        {
            throw new ArgumentOutOfRangeException(nameof(packetIndex), packetIndex, "Packet index is outside the frame.");
        }

        var start = packetIndex * StreamLedsPerPacket;
        var count = Math.Min(StreamLedsPerPacket, colors.Length - start);
        packet.Clear();
        packet[0] = ReportId;
        packet[1] = zone;
        packet[2] = (byte)(start & 0xFF);
        packet[3] = (byte)(start >> 8);
        packet[4] = (byte)count;
        packet[5] = SteadyEffect;
        packet[6] = 0x00;
        for (var i = 0; i < count; i++)
        {
            var color = colors[start + i];
            packet[StreamHeaderLength + (i * 3)] = color.R;
            packet[StreamHeaderLength + (i * 3) + 1] = color.G;
            packet[StreamHeaderLength + (i * 3) + 2] = color.B;
        }
    }

    private static void WriteSteadyZone(Span<byte> zone, RgbColor color)
    {
        zone[0] = SteadyEffect;
        zone[1] = color.R;
        zone[2] = color.G;
        zone[3] = color.B;
        zone[4] = FullBrightnessFlags;
        zone[8] = ColorEnabledFlag;
    }
}
