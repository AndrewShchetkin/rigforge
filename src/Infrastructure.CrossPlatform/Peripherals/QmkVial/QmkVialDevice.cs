using HidSharp;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Hid;

namespace RigForge.Infrastructure.CrossPlatform.Peripherals.QmkVial;

/// <summary>
/// QMK keyboard with VialRGB direct mode. HSV packets are built in a stack buffer.
/// Firmware that answers VIA but was built without <c>VIALRGB_ENABLE</c> is left closed.
/// </summary>
internal sealed class QmkVialDevice : IRgbDevice
{
    private const int HandshakeTimeoutMs = 250;

    private readonly HidReportChannel _hid;
    private readonly bool _dryRun;
    private readonly Lock _transaction = new();
    private readonly Dictionary<string, string> _properties;
    private bool _directModeSent;
    private bool _disposed;

    private QmkVialDevice(HidReportChannel hid, string id, string name, string vendor, LedPosition[] positions, bool dryRun, string usbPath, int productId)
    {
        _hid = hid;
        _dryRun = dryRun;
        Id = id;
        Name = name;
        Vendor = vendor.Length > 0 ? vendor : "QMK";
        Zones =
        [
            new RgbZone
            {
                Id = "matrix",
                Name = "RGB matrix",
                Topology = LedTopology.Matrix2D,
                Leds = positions,
                IsResizable = false,
                MaxLedCount = positions.Length,
            },
        ];
        _properties = new Dictionary<string, string>
        {
            ["ProductId"] = $"0x{productId:X4}",
            ["vidPid"] = UsbVidPid.Format(hid.Device, 0, productId),
            ["UsbPath"] = usbPath,
            ["UsagePage"] = $"0x{QmkVialProtocol.UsagePage:X4}",
            ["LedCount"] = positions.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    public string Id { get; }

    public string Name { get; }

    public string Vendor { get; }

    public DeviceCapabilities Capabilities => DeviceCapabilities.Rgb;

    public IReadOnlyDictionary<string, string> Properties => _properties;

    public IReadOnlyList<RgbZone> Zones { get; }

    public int MaxFramesPerSecond => 60;

    public static bool IsRawHidInterface(HidDevice device)
    {
        if (SafeLength(() => device.GetMaxOutputReportLength()) < 32)
        {
            return false;
        }

        foreach (var packed in device.SafeTopLevelUsages())
        {
            if ((packed >> 16) == QmkVialProtocol.UsagePage && (packed & 0xFFFF) == QmkVialProtocol.Usage)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Opens the raw HID interface, checks VIA, then VialRGB.
    /// <paramref name="vialRgbDisabled"/> is set when the board speaks VIA but direct RGB is not available.
    /// </summary>
    public static QmkVialDevice? TryOpen(HidDevice hidDevice, string id, bool dryRun, out bool vialRgbDisabled)
    {
        vialRgbDisabled = false;
        var hid = HidReportChannel.Open(hidDevice);
        try
        {
            hid.DrainInput();
            if (!QueryVia(hid))
            {
                hid.Dispose();
                return null;
            }

            if (VialRgbExplicitlyDisabled(hid))
            {
                vialRgbDisabled = true;
                hid.Dispose();
                return null;
            }

            if (!TryReadVialRgb(hid, out var version, out var maxBrightness, out var ledCount) || version < 1 || ledCount <= 0)
            {
                vialRgbDisabled = true;
                hid.Dispose();
                return null;
            }

            var positions = ReadPositions(hid, ledCount);
            if (!dryRun)
            {
                Span<byte> mode = stackalloc byte[QmkVialProtocol.ReportLength];
                QmkVialProtocol.WriteDirectMode(maxBrightness == 0 ? (byte)255 : maxBrightness, mode);
                hid.Write(mode);
            }

            var product = hidDevice.SafeProductName();
            var vendor = hidDevice.SafeManufacturer();
            var device = new QmkVialDevice(
                hid,
                id,
                product.Length > 0 ? product : "QMK VialRGB",
                vendor,
                positions,
                dryRun,
                hidDevice.DevicePath,
                hidDevice.ProductID)
            {
                _directModeSent = !dryRun,
            };
            device._properties["VialRgbVersion"] = version.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return device;
        }
        catch
        {
            hid.Dispose();
            throw;
        }
    }

    public bool TryResizeZone(string zoneId, int ledCount) => false;

    public ValueTask UpdateLedsAsync(ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_dryRun || colors.IsEmpty)
        {
            return ValueTask.CompletedTask;
        }

        lock (_transaction)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Span<byte> packet = stackalloc byte[QmkVialProtocol.ReportLength];
            if (!_directModeSent)
            {
                QmkVialProtocol.WriteDirectMode(255, packet);
                _hid.Write(packet);
                _directModeSent = true;
            }

            var span = colors.Span;
            var start = 0;
            while (start < span.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(QmkVialProtocol.LedsPerPacket, span.Length - start);
                QmkVialProtocol.WriteDirectPacket((ushort)start, span.Slice(start, count), packet);
                _hid.Write(packet);
                start += count;
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        lock (_transaction)
        {
            if (!_disposed)
            {
                _disposed = true;
                _hid.Dispose();
            }
        }

        return ValueTask.CompletedTask;
    }

    private static bool QueryVia(HidReportChannel hid)
    {
        Span<byte> request = stackalloc byte[QmkVialProtocol.ReportLength];
        QmkVialProtocol.WriteGetProtocolVersion(request);
        var response = hid.Request(request, static buffer => QmkVialProtocol.IsViaProtocolResponse(buffer), HandshakeTimeoutMs);
        return response is not null && QmkVialProtocol.IsViaProtocolResponse(response);
    }

    private static bool VialRgbExplicitlyDisabled(HidReportChannel hid)
    {
        Span<byte> request = stackalloc byte[QmkVialProtocol.ReportLength];
        QmkVialProtocol.WriteGetVialKeyboardId(request);
        var response = hid.Request(request, static buffer => buffer.Length >= 13, HandshakeTimeoutMs);
        return response is not null
            && QmkVialProtocol.TryGetVialRgbCompiledFlag(response, out var compiled)
            && !compiled;
    }

    private static bool TryReadVialRgb(HidReportChannel hid, out ushort version, out byte maxBrightness, out int ledCount)
    {
        version = 0;
        maxBrightness = 0;
        ledCount = 0;
        Span<byte> request = stackalloc byte[QmkVialProtocol.ReportLength];
        QmkVialProtocol.WriteGetVialRgbInfo(request);
        var info = hid.Request(request, static buffer => QmkVialProtocol.TryParseVialRgbInfo(buffer, out _, out _), HandshakeTimeoutMs);
        if (info is null || !QmkVialProtocol.TryParseVialRgbInfo(info, out version, out maxBrightness))
        {
            return false;
        }

        QmkVialProtocol.WriteGetLedCount(request);
        var count = hid.Request(request, static buffer => QmkVialProtocol.TryParseLedCount(buffer, out _), HandshakeTimeoutMs);
        return count is not null && QmkVialProtocol.TryParseLedCount(count, out ledCount);
    }

    private static LedPosition[] ReadPositions(HidReportChannel hid, int ledCount)
    {
        var positions = new LedPosition[ledCount];
        var linear = LedLayouts.Linear(ledCount);
        Span<byte> request = stackalloc byte[QmkVialProtocol.ReportLength];
        for (var i = 0; i < ledCount; i++)
        {
            QmkVialProtocol.WriteGetLedInfo((ushort)i, request);
            var response = hid.Request(request, static buffer => QmkVialProtocol.TryParseLedInfo(buffer, out _, out _, out _), HandshakeTimeoutMs);
            if (response is null || !QmkVialProtocol.TryParseLedInfo(response, out var x, out var y, out _))
            {
                for (var rest = i; rest < ledCount; rest++)
                {
                    positions[rest] = linear[rest];
                }

                break;
            }

            positions[i] = QmkVialProtocol.ToLedPosition(x, y);
        }

        return positions;
    }

    private static int SafeLength(Func<int> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
