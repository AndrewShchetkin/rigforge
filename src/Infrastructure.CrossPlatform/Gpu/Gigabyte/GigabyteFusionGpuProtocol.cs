using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.Gpu.Gigabyte;

internal enum GigabyteGpuGeneration
{
    Gen4,
    Gen2,
}

/// <summary>One lighting zone: a human readable name plus the hardware zone id it is wired to.</summary>
internal readonly record struct GigabyteZoneInfo(string Id, string Name, byte HardwareZone);

/// <summary>Per-variant wiring: the Gen4 packet header (5 bytes) and the hardware zones exposed as point LEDs.</summary>
internal sealed record GigabyteGpuLayout(GigabyteGpuGeneration Generation, string Model, byte[] Header, GigabyteZoneInfo[] Zones);

/// <summary>
/// Gigabyte RGB Fusion GPU controllers. Gen4 (<c>0x75</c>) takes one 10-byte raw packet per zone
/// <c>[header(5), R, G, B, 0x00, zone]</c>; Gen2 (<c>0x71</c>) takes 8-byte raw packets. All writes are volatile.
/// </summary>
internal static class GigabyteFusionGpuProtocol
{
    public const byte Gen4SlaveAddress = 0x75;
    public const byte Gen2SlaveAddress = 0x71;
    public const int Gen4PacketLength = 10;
    public const int Gen2PacketLength = 8;
    public const int HardwareZoneCount = 6;

    /// <summary>Gen2 hardware zones are addressed as <c>0xB0 + zone * 4 + packetIndex</c>.</summary>
    public const int Gen2PacketsPerZone = 1;

    public static readonly TimeSpan InterWriteDelay = TimeSpan.FromMilliseconds(9);

    private const string DefaultVariant = "Gen4:GAMING";

    private static readonly byte[] GamingHeader = [0x12, 0x01, 0x01, 0x06, 0x0A];
    private static readonly byte[] EagleHeader = [0x16, 0x01, 0x00, 0x06, 0x00];

    /// <summary>Parses <c>"Gen4:MODEL"</c> / <c>"Gen2:MODEL"</c>; unknown Gen4 models use the GAMING header with zones 0..3.</summary>
    public static GigabyteGpuLayout ParseVariant(string? variant)
    {
        var text = string.IsNullOrWhiteSpace(variant) ? DefaultVariant : variant.Trim();
        var separator = text.IndexOf(':', StringComparison.Ordinal);
        var generation = (separator < 0 ? text : text[..separator]).Equals("Gen2", StringComparison.OrdinalIgnoreCase)
            ? GigabyteGpuGeneration.Gen2
            : GigabyteGpuGeneration.Gen4;
        var model = (separator < 0 ? string.Empty : text[(separator + 1)..]).Trim().ToUpperInvariant();

        if (generation == GigabyteGpuGeneration.Gen2)
        {
            return model == "SINGLE_GEN2"
                ? new GigabyteGpuLayout(generation, model, [], [Zone("gpu", "GPU", 0)])
                : new GigabyteGpuLayout(
                    generation,
                    string.IsNullOrEmpty(model) ? "GAMING_GEN2" : model,
                    [],
                    [Zone("fan-1", "Fan 1", 0), Zone("fan-2", "Fan 2", 1), Zone("fan-3", "Fan 3", 2), Zone("logo", "Logo", 3)]);
        }

        return model switch
        {
            "EAGLE" => new GigabyteGpuLayout(generation, model, EagleHeader, [Zone("side-strip", "Side Strip", 0)]),
            "AERO" => new GigabyteGpuLayout(generation, model, EagleHeader, [Zone("side-logo", "Side Logo", 0)]),
            "GAMING" => new GigabyteGpuLayout(generation, model, GamingHeader, [Zone("fan-1", "Fan 1", 1), Zone("fan-2", "Fan 2", 2), Zone("fan-3", "Fan 3", 0), Zone("logo", "Logo", 3)]),
            "AORUS MASTER" => new GigabyteGpuLayout(generation, model, GamingHeader, [Zone("fan-1", "Fan 1", 1), Zone("fan-2", "Fan 2", 2), Zone("fan-3", "Fan 3", 0), Zone("logo", "Logo", 3), Zone("back-logo", "Back Logo", 5)]),
            "WATERFORCE" => new GigabyteGpuLayout(generation, model, GamingHeader, [Zone("logo", "Logo", 0), Zone("fans", "Fans", 2), Zone("front-text", "Front Text", 3), Zone("side-text", "Side Text", 4)]),
            "WATERFORCE XTREME" => new GigabyteGpuLayout(generation, model, GamingHeader, [Zone("logo", "Logo", 0), Zone("front-text", "Front Text", 1), Zone("fans", "Fans", 2), Zone("accent", "Accent", 3), Zone("side-text", "Side Text", 4)]),
            _ => new GigabyteGpuLayout(generation, string.IsNullOrEmpty(model) ? "GAMING" : model, GamingHeader, [Zone("zone-1", "Zone 1", 0), Zone("zone-2", "Zone 2", 1), Zone("zone-3", "Zone 3", 2), Zone("zone-4", "Zone 4", 3)]),
        };
    }

    /// <summary>
    /// Gen2 presence check: a successful <c>0xAB</c> query write, or (when the write is refused) a read-back
    /// <c>[0xAB, code, ...]</c> with a known controller code.
    /// </summary>
    public static bool TryProbeGen2(IGpuI2cBus bus, byte slaveAddress)
    {
        ReadOnlySpan<byte> query = [0xAB, 0, 0, 0, 0, 0, 0, 0];
        return bus.TryWriteRaw(slaveAddress, query) || TryProbeGen2ReadOnly(bus, slaveAddress);
    }

    /// <summary>Read-only variant of <see cref="TryProbeGen2"/> used in dry-run mode.</summary>
    public static bool TryProbeGen2ReadOnly(IGpuI2cBus bus, byte slaveAddress)
    {
        Span<byte> response = stackalloc byte[Gen2PacketLength];
        return bus.TryReadRaw(slaveAddress, response)
            && response[0] == 0xAB
            && response[1] is 0x10 or 0x11 or 0x12 or 0x14;
    }

    /// <summary>Blanks every Gen4 hardware zone (0..5) that is not mapped to a logical zone. Returns false on the first failed write.</summary>
    public static bool TryInitializeGen4(IGpuI2cBus bus, byte slaveAddress, GigabyteGpuLayout layout, IPrecisionTimer timer)
    {
        Span<byte> packet = [0x12, 0x01, 0x01, 0x06, 0x0A, 0, 0, 0, 0, 0];
        var first = true;
        for (byte zone = 0; zone < HardwareZoneCount; zone++)
        {
            if (IsMapped(layout, zone))
            {
                continue;
            }

            if (!first)
            {
                timer.Sleep(InterWriteDelay);
            }

            first = false;
            packet[9] = zone;
            if (!bus.TryWriteRaw(slaveAddress, packet))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Static mode for Gen2 controllers.</summary>
    public static bool TryInitializeGen2(IGpuI2cBus bus, byte slaveAddress)
    {
        ReadOnlySpan<byte> staticMode = [0x88, 0x01, 0x06, 0x63, 0x08, 0x00];
        return bus.TryWriteRaw(slaveAddress, staticMode);
    }

    public static bool TryWriteGen4Zone(IGpuI2cBus bus, byte slaveAddress, GigabyteGpuLayout layout, byte hardwareZone, RgbColor color)
    {
        Span<byte> packet = stackalloc byte[Gen4PacketLength];
        layout.Header.CopyTo(packet);
        packet[5] = color.R;
        packet[6] = color.G;
        packet[7] = color.B;
        packet[8] = 0x00;
        packet[9] = hardwareZone;
        return bus.TryWriteRaw(slaveAddress, packet);
    }

    public static bool TryWriteGen2Zone(IGpuI2cBus bus, byte slaveAddress, byte hardwareZone, RgbColor color)
    {
        Span<byte> packet = stackalloc byte[Gen2PacketLength];
        packet[0] = (byte)(0xB0 + hardwareZone * 4);
        packet[1] = 0x01;
        packet[2] = color.R;
        packet[3] = color.G;
        packet[4] = color.B;
        packet[5] = color.R;
        packet[6] = color.G;
        packet[7] = color.B;
        return bus.TryWriteRaw(slaveAddress, packet);
    }

    private static bool IsMapped(GigabyteGpuLayout layout, byte hardwareZone)
    {
        foreach (var zone in layout.Zones)
        {
            if (zone.HardwareZone == hardwareZone)
            {
                return true;
            }
        }

        return false;
    }

    private static GigabyteZoneInfo Zone(string id, string name, byte hardwareZone) => new(id, name, hardwareZone);
}
