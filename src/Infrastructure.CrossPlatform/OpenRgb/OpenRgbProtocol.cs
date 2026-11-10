using System.Buffers.Binary;
using System.Net;
using System.Text;
using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.OpenRgb;

/// <summary>One controller decoded from an OpenRGB SDK controller-data packet.</summary>
public readonly record struct OpenRgbControllerInfo(
    string Name,
    string Vendor,
    string Description,
    string Version,
    string Serial,
    string Location,
    int ZoneCount,
    int LedCount);

/// <summary>
/// OpenRGB SDK binary framing: <c>"ORGB"</c> + little-endian device index, packet id, and data length.
/// LED updates pack each color as RGBX (<c>R, G, B, 0x00</c>). The bridge is opt-in.
/// </summary>
public static class OpenRgbProtocol
{
    public const int HeaderLength = 16;
    public const int DefaultPort = 6742;
    public const uint ClientProtocolVersion = 4;
    public const int MaxLeds = 1024;
    public const int MaxControllers = 32;

    public const uint RequestControllerCount = 0;
    public const uint RequestControllerData = 1;
    public const uint RequestProtocolVersion = 40;
    public const uint SetClientName = 50;
    public const uint UpdateLeds = 1050;
    public const uint SetCustomMode = 1100;

    public static ReadOnlySpan<byte> ClientName => "RigForge"u8;

    public static bool TryGetEndpoint(IConfigurationView? configuration, out string host, out int port)
    {
        var env = Environment.GetEnvironmentVariable("RIGFORGE_OPENRGB_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return TryParseEndpoint(env.Trim(), out host, out port);
        }

        host = IPAddress.Loopback.ToString();
        port = DefaultPort;
        if (!string.Equals(configuration?["Hardware:OpenRgb:Enabled"], "true", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var configured = configuration?["Hardware:OpenRgb:Endpoint"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return true;
        }

        return TryParseEndpoint(configured.Trim(), out host, out port);
    }

    public static bool TryParseEndpoint(string text, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        var colon = text.LastIndexOf(':');
        if (colon <= 0 || colon >= text.Length - 1)
        {
            return false;
        }

        host = text[..colon];
        return host.Length > 0
            && int.TryParse(text[(colon + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out port)
            && port is > 0 and <= 65535;
    }

    public static void WriteHeader(Span<byte> destination, uint deviceIndex, uint packetId, int dataLength)
    {
        if (destination.Length < HeaderLength)
        {
            throw new ArgumentException($"An OpenRGB header needs {HeaderLength} bytes.", nameof(destination));
        }

        if (dataLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dataLength));
        }

        destination[0] = (byte)'O';
        destination[1] = (byte)'R';
        destination[2] = (byte)'G';
        destination[3] = (byte)'B';
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], deviceIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], packetId);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], (uint)dataLength);
    }

    public static bool TryParseHeader(ReadOnlySpan<byte> header, out uint deviceIndex, out uint packetId, out int dataLength)
    {
        deviceIndex = 0;
        packetId = 0;
        dataLength = 0;
        if (header.Length < HeaderLength
            || header[0] != (byte)'O'
            || header[1] != (byte)'R'
            || header[2] != (byte)'G'
            || header[3] != (byte)'B')
        {
            return false;
        }

        deviceIndex = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        packetId = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        var rawLength = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
        if (rawLength > 1_048_576)
        {
            return false;
        }

        dataLength = (int)rawLength;
        return true;
    }

    public static int WriteProtocolVersionRequest(Span<byte> destination)
    {
        WriteHeader(destination, 0, RequestProtocolVersion, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[HeaderLength..], ClientProtocolVersion);
        return HeaderLength + 4;
    }

    public static int WriteClientName(Span<byte> destination)
    {
        var length = ClientName.Length + 1;
        WriteHeader(destination, 0, SetClientName, length);
        ClientName.CopyTo(destination[HeaderLength..]);
        destination[HeaderLength + ClientName.Length] = 0;
        return HeaderLength + length;
    }

    public static int WriteSimple(Span<byte> destination, uint deviceIndex, uint packetId)
    {
        WriteHeader(destination, deviceIndex, packetId, 0);
        return HeaderLength;
    }

    /// <summary>
    /// <c>RGBCONTROLLER_UPDATELEDS</c>: header plus <c>ledCount * 4</c> RGBX bytes. Allocation-free.
    /// </summary>
    public static int WriteUpdateLeds(uint deviceIndex, ReadOnlySpan<RgbColor> colors, Span<byte> destination)
    {
        var dataLength = colors.Length * 4;
        if (destination.Length < HeaderLength + dataLength)
        {
            throw new ArgumentException("The OpenRGB send buffer is smaller than the LED frame.", nameof(destination));
        }

        WriteHeader(destination, deviceIndex, UpdateLeds, dataLength);
        for (var i = 0; i < colors.Length; i++)
        {
            var offset = HeaderLength + (i * 4);
            destination[offset] = colors[i].R;
            destination[offset + 1] = colors[i].G;
            destination[offset + 2] = colors[i].B;
            destination[offset + 3] = 0;
        }

        return HeaderLength + dataLength;
    }

    /// <summary>
    /// Writes a protocol-version-4 controller-data blob (length-prefixed strings, one direct mode, zones, LEDs).
    /// Returns the number of bytes written.
    /// </summary>
    public static int WriteControllerData(in OpenRgbControllerInfo info, Span<byte> destination)
    {
        if (info.LedCount is < 0 or > MaxLeds || info.ZoneCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(info));
        }

        var offset = 0;
        WriteText(destination, ref offset, info.Name);
        WriteText(destination, ref offset, info.Vendor);
        WriteText(destination, ref offset, info.Description);
        WriteText(destination, ref offset, info.Version);
        WriteText(destination, ref offset, info.Serial);
        WriteText(destination, ref offset, info.Location);

        BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], 1);
        offset += 2;
        WriteText(destination, ref offset, "Direct");
        Clear(destination, ref offset, 48);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], 0);
        offset += 2;

        BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], 0);
        offset += 4;

        BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], (ushort)info.ZoneCount);
        offset += 2;
        var perZone = info.LedCount / info.ZoneCount;
        var remainder = info.LedCount % info.ZoneCount;
        for (var zone = 0; zone < info.ZoneCount; zone++)
        {
            var leds = perZone + (zone < remainder ? 1 : 0);
            WriteText(destination, ref offset, zone == 0 ? "Lighting" : $"Zone {zone + 1}");
            BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], 1);
            offset += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(destination[offset..], 0);
            offset += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(destination[offset..], (uint)Math.Max(leds, 1));
            offset += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(destination[offset..], (uint)leds);
            offset += 4;
            BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], 0);
            offset += 2;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], (ushort)info.LedCount);
        offset += 2;
        for (var led = 0; led < info.LedCount; led++)
        {
            WriteText(destination, ref offset, "LED");
            Clear(destination, ref offset, 4);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], (ushort)info.LedCount);
        offset += 2;
        Clear(destination, ref offset, info.LedCount * 4);
        return offset;
    }

    private static void Clear(Span<byte> destination, ref int offset, int count)
    {
        if (count < 0 || (uint)offset + (uint)count > (uint)destination.Length)
        {
            throw new ArgumentException("The controller-data buffer is too small.", nameof(destination));
        }

        destination.Slice(offset, count).Clear();
        offset += count;
    }

    public static bool TryParseControllerData(ReadOnlySpan<byte> data, out OpenRgbControllerInfo info)
    {
        info = default;
        var offset = 0;
        if (!TryReadText(data, ref offset, out var name)
            || !TryReadText(data, ref offset, out var vendor)
            || !TryReadText(data, ref offset, out var description)
            || !TryReadText(data, ref offset, out var version)
            || !TryReadText(data, ref offset, out var serial)
            || !TryReadText(data, ref offset, out var location)
            || offset + 2 > data.Length)
        {
            return false;
        }

        var modeCount = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        offset += 2;
        for (var mode = 0; mode < modeCount; mode++)
        {
            if (!SkipMode(data, ref offset))
            {
                return false;
            }
        }

        if (offset + 4 + 2 > data.Length)
        {
            return false;
        }

        offset += 4; // active mode
        var zoneCount = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        offset += 2;
        var ledsFromZones = 0;
        for (var zone = 0; zone < zoneCount; zone++)
        {
            if (!TryReadText(data, ref offset, out _) || offset + 16 + 2 > data.Length)
            {
                return false;
            }

            offset += 4; // type
            offset += 4; // min
            offset += 4; // max
            ledsFromZones += (int)BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;
            var matrix = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
            offset += 2;
            if (offset + matrix > data.Length)
            {
                return false;
            }

            offset += matrix;
        }

        if (offset + 2 > data.Length)
        {
            return false;
        }

        var ledCount = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        offset += 2;
        if (ledCount > MaxLeds)
        {
            return false;
        }

        for (var led = 0; led < ledCount; led++)
        {
            if (!TryReadText(data, ref offset, out _) || offset + 4 > data.Length)
            {
                return false;
            }

            offset += 4;
        }

        if (ledCount == 0)
        {
            ledCount = (ushort)Math.Clamp(ledsFromZones, 0, MaxLeds);
        }

        info = new OpenRgbControllerInfo(name, vendor, description, version, serial, location, zoneCount, ledCount);
        return ledCount > 0 && name.Length > 0;
    }

    private static bool SkipMode(ReadOnlySpan<byte> data, ref int offset)
    {
        if (!TryReadText(data, ref offset, out _) || offset + 48 + 2 > data.Length)
        {
            return false;
        }

        offset += 48;
        var colors = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        offset += 2;
        var bytes = colors * 4;
        if (offset + bytes > data.Length)
        {
            return false;
        }

        offset += bytes;
        return true;
    }

    private static void WriteText(Span<byte> destination, ref int offset, string text)
    {
        var encoded = Encoding.UTF8.GetBytes(text);
        var length = encoded.Length + 1;
        if (offset + 2 + length > destination.Length)
        {
            throw new ArgumentException("The controller-data buffer is too small.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], (ushort)length);
        offset += 2;
        encoded.CopyTo(destination[offset..]);
        offset += encoded.Length;
        destination[offset++] = 0;
    }

    private static bool TryReadText(ReadOnlySpan<byte> data, ref int offset, out string value)
    {
        value = string.Empty;
        if (offset + 2 > data.Length)
        {
            return false;
        }

        var length = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        offset += 2;
        if (length > 512 || offset + length > data.Length)
        {
            return false;
        }

        var slice = data.Slice(offset, length);
        offset += length;
        var end = slice.IndexOf((byte)0);
        if (end >= 0)
        {
            slice = slice[..end];
        }

        value = Encoding.UTF8.GetString(slice);
        return true;
    }
}

/// <summary>Narrow view so the protocol can read configuration without a package-specific helper.</summary>
public interface IConfigurationView
{
    string? this[string key] { get; }
}
