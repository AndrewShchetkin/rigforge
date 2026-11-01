using Microsoft.Extensions.Logging;
using RigForge.Infrastructure.CrossPlatform.Hid;
using RigForge.Infrastructure.CrossPlatform.Usb;

namespace RigForge.Infrastructure.CrossPlatform.Nzxt;

/// <summary>
/// Uploads frames to the Kraken Z3/2023 screen: control messages over HID, pixels over the bulk endpoint.
/// Two firmware generations are handled, mirroring liquidctl's kraken3.py:
/// <list type="bullet">
/// <item>Kraken 2023 firmware 2.x: RGB565 frame written straight to the framebuffer (fast, suitable for streaming).</item>
/// <item>Everything else: RGBX frame stored in one of 16 memory "buckets", then made active.</item>
/// </list>
/// Bucket memory is RAM (configuration is kept only while the cooler has power), so frequent uploads cause no wear.
/// </summary>
internal sealed partial class KrakenLcdUploader(
    HidReportChannel hid,
    IUsbBulkTransport bulk,
    int width,
    int height,
    bool useRgb565Stream,
    ILogger logger) : IDisposable
{
    private static readonly byte[] BulkMagic = [0x12, 0xFA, 0x01, 0xE8, 0xAB, 0xCD, 0xEF, 0x98, 0x76, 0x54, 0x32, 0x10];
    private const int BucketCount = 16;
    private const int TotalMemoryKb = 24320;
    private const int ResponseTimeoutMs = 400;

    private readonly byte[] _payload = new byte[width * height * (useRgb565Stream ? 2 : 4)];
    private bool _primed;
    private bool _bucketsInitialized;
    private int _activeBucket = -1;

    public bool UsesRgb565Stream => useRgb565Stream;

    public void Upload(ReadOnlySpan<byte> rgba)
    {
        if (rgba.Length < width * height * 4)
        {
            throw new ArgumentException($"Expected a {width}x{height} RGBA frame.", nameof(rgba));
        }

        if (useRgb565Stream)
        {
            ConvertToRgb565(rgba, _payload);
            SendFramebuffer();
            if (!_primed)
            {
                // The firmware shows the first frame after (re)initialization only when it is sent twice.
                SendFramebuffer();
                _primed = true;
            }
        }
        else
        {
            ConvertToRgbx(rgba, _payload);
            SendToBucket();
        }
    }

    /// <summary>Switches to the firmware's liquid temperature screen.</summary>
    public void ShowLiquidScreen()
    {
        SwitchBucket(0, mode: 0x02);
        _primed = false;
        _bucketsInitialized = false;
        _activeBucket = -1;
    }

    public void Dispose() => bulk.Dispose();

    internal static void ConvertToRgb565(ReadOnlySpan<byte> rgba, Span<byte> destination)
    {
        for (int source = 0, target = 0; target + 1 < destination.Length; source += 4, target += 2)
        {
            var r = rgba[source];
            var g = rgba[source + 1];
            var b = rgba[source + 2];
            destination[target] = (byte)((r & 0xF8) | (g >> 5));
            destination[target + 1] = (byte)(((g << 3) & 0xE0) | (b >> 3));
        }
    }

    internal static void ConvertToRgbx(ReadOnlySpan<byte> rgba, Span<byte> destination)
    {
        rgba[..destination.Length].CopyTo(destination);
        for (var i = 3; i < destination.Length; i += 4)
        {
            destination[i] = 0;
        }
    }

    internal static byte[] BulkHeader(byte mode, int payloadLength)
    {
        var header = new byte[BulkMagic.Length + 8];
        BulkMagic.CopyTo(header, 0);
        header[BulkMagic.Length] = mode;
        BitConverter.TryWriteBytes(header.AsSpan(BulkMagic.Length + 4), (uint)payloadLength);
        return header;
    }

    private void SendFramebuffer()
    {
        Command([0x36, 0x01, 0x00, 0x01, 0x06]);
        bulk.Write(BulkHeader(0x06, _payload.Length));
        bulk.Write(_payload);
        Command([0x36, 0x02]);
    }

    private void SendToBucket()
    {
        Command([0x36, 0x03]);

        var index = _activeBucket == 0 ? 1 : 0;
        if (!_bucketsInitialized)
        {
            DeleteAllBuckets();
            _bucketsInitialized = true;
        }
        else if (_activeBucket >= 0 || _bucketsInitialized)
        {
            DeleteBucket(index);
        }

        var header = BulkHeader(0x02, _payload.Length);
        var sizeKb = (int)Math.Ceiling((header.Length + _payload.Length) / 1024.0);
        var memoryStart = index * sizeKb;

        var setup = Command([0x32, 0x01, (byte)index, (byte)(index + 1), (byte)memoryStart, (byte)(memoryStart >> 8), (byte)sizeKb, (byte)(sizeKb >> 8), 0x01]);
        if (setup is null || setup[14] != 0x01)
        {
            LogBucketSetupFailed(index);
        }

        Command([0x36, 0x01, (byte)index]);
        bulk.Write(header);
        bulk.Write(_payload);
        hid.Write([0x36, 0x02]);

        if (!SwitchBucket(index, mode: 0x04))
        {
            throw new IOException($"The Kraken refused to display bucket {index}.");
        }

        _activeBucket = index;
    }

    /// <summary>Port of liquidctl's _get_bucket_memory_offset (sizes and offsets in KiB).</summary>
    internal static int BucketMemoryOffset(byte[][] buckets, int index, int sizeKb)
    {
        static int Start(byte[] b) => b[17] | b[18] << 8;
        static int Size(byte[] b) => b[19] | b[20] << 8;

        var current = buckets[index];
        var currentStart = Start(current);
        if (sizeKb <= Size(current))
        {
            return currentStart;
        }

        var minOccupied = currentStart;
        var maxOccupied = 0;
        var overlaps = false;
        for (var i = 0; i < buckets.Length; i++)
        {
            var start = Start(buckets[i]);
            var end = start + Size(buckets[i]);
            maxOccupied = Math.Max(maxOccupied, end);
            minOccupied = Math.Min(minOccupied, start);
            if ((start > currentStart && start < currentStart + sizeKb)
                || (start < currentStart && end > start)
                || (start == currentStart && i != index))
            {
                overlaps = true;
            }
        }

        if (!overlaps)
        {
            return currentStart;
        }

        if (maxOccupied + sizeKb < TotalMemoryKb)
        {
            return maxOccupied;
        }

        return sizeKb < minOccupied ? 0 : -1;
    }

    private bool DeleteBucket(int index)
    {
        hid.Write([0x32, 0x02, (byte)index]);
        var response = hid.ReadUntil(r => NzxtProtocol.IsResponse(r, 0x33, 0x02), ResponseTimeoutMs);
        return response is not null && response[14] == 0x01;
    }

    private void DeleteAllBuckets()
    {
        SwitchBucket(0, mode: 0x02);
        for (var i = 0; i < BucketCount; i++)
        {
            DeleteBucket(i);
        }
    }

    private bool SwitchBucket(int index, byte mode)
    {
        var response = Command([0x38, 0x01, mode, (byte)index]);
        return response is not null && response[14] == 0x01;
    }

    /// <summary>Sends a control message and waits briefly for its echo (command + 1). Missing echoes are tolerated.</summary>
    private byte[]? Command(byte[] request)
    {
        var response = hid.Request(request, r => NzxtProtocol.IsResponse(r, (byte)(request[0] + 1), request[1]), ResponseTimeoutMs);
        if (response is null)
        {
            LogNoResponse(request[0], request[1]);
        }

        return response;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "No response to Kraken command {Command:X2} {SubCommand:X2}")]
    private partial void LogNoResponse(byte command, byte subCommand);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kraken screen bucket {Index} setup was not acknowledged")]
    private partial void LogBucketSetupFailed(int index);
}
