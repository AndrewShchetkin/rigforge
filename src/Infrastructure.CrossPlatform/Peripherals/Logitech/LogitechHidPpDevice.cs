using System.Globalization;
using HidSharp;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Hid;

namespace RigForge.Infrastructure.CrossPlatform.Peripherals.Logitech;

/// <summary>
/// Logitech Lightsync mouse, keyboard, or headset on HID++ 2.0.
/// Two catalog zones (primary and logo) are streamed as fixed effects.
/// Dry-run and a missing IRoot reply never send <c>setZoneEffect</c>.
/// </summary>
internal sealed class LogitechHidPpDevice : IRgbDevice
{
    private const int QueryTimeoutMs = 300;

    private readonly HidReportChannel? _hid;
    private readonly byte _deviceIndex;
    private readonly byte _featureIndex;
    private readonly byte _zoneFunction;
    private readonly bool _dryRun;
    private readonly Lock _transaction = new();
    private readonly Dictionary<string, string> _properties;
    private bool _disposed;

    private LogitechHidPpDevice(
        HidReportChannel? hid,
        string id,
        string name,
        string usbPath,
        int productId,
        byte deviceIndex,
        byte featureIndex,
        bool rgbEffects8071,
        bool dryRun,
        bool online)
    {
        _hid = hid;
        _deviceIndex = deviceIndex;
        _featureIndex = featureIndex;
        _zoneFunction = rgbEffects8071 ? LogitechHidPpProtocol.SetZoneEffectFunction : LogitechHidPpProtocol.SetZoneEffectFunction8070;
        _dryRun = dryRun;
        Id = id;
        Name = name;
        Zones =
        [
            Zone("primary", "Primary Zone"),
            Zone("logo", "Logo Zone"),
        ];
        _properties = new Dictionary<string, string>
        {
            ["ProductId"] = $"0x{productId:X4}",
            ["vidPid"] = UsbVidPid.Format(hid?.Device, LogitechHidPpProtocol.VendorId, productId),
            ["UsbPath"] = usbPath,
            ["DeviceIndex"] = $"0x{deviceIndex:X2}",
            ["FeatureIndex"] = featureIndex.ToString(CultureInfo.InvariantCulture),
            ["Link"] = online ? "online" : "catalog",
        };
        if (!online)
        {
            _properties["Status"] = "HID++ did not answer; zones are catalog defaults and no effect was sent";
        }
    }

    public string Id { get; }

    public string Name { get; }

    public string Vendor => "Logitech";

    public DeviceCapabilities Capabilities => DeviceCapabilities.Rgb;

    public string? StatusReason =>
        _properties.TryGetValue("Status", out var status) ? status : null;

    public IReadOnlyDictionary<string, string> Properties => _properties;

    public IReadOnlyList<RgbZone> Zones { get; }

    public int MaxFramesPerSecond => 30;

    public bool IsOnline => _hid is not null && _featureIndex != 0;

    public static bool IsLongReportInterface(HidDevice device)
    {
        if (SafeLength(() => device.GetMaxOutputReportLength()) < LogitechHidPpProtocol.ReportLength)
        {
            return false;
        }

        foreach (var packed in device.SafeTopLevelUsages())
        {
            if ((packed >> 16) == LogitechHidPpProtocol.UsagePage && (packed & 0xFFFF) == LogitechHidPpProtocol.Usage)
            {
                return true;
            }
        }

        return false;
    }

    public static LogitechHidPpDevice Open(HidDevice hidDevice, string id, bool dryRun)
    {
        var product = hidDevice.SafeProductName();
        var name = product.Length > 0 ? product : "Logitech Lightsync";
        HidReportChannel? hid = null;
        try
        {
            hid = HidReportChannel.Open(hidDevice);
            hid.DrainInput();
            if (!TryResolveFeature(hid, out var deviceIndex, out var featureIndex, out var rgbEffects))
            {
                hid.Dispose();
                return new LogitechHidPpDevice(null, id, name, hidDevice.DevicePath, hidDevice.ProductID, LogitechHidPpProtocol.WiredDeviceIndex, 0, true, dryRun, online: false);
            }

            if (!dryRun)
            {
                Span<byte> control = stackalloc byte[LogitechHidPpProtocol.ReportLength];
                LogitechHidPpProtocol.WriteSoftwareControl(deviceIndex, featureIndex, rgbEffects, control);
                hid.Write(control);
            }

            return new LogitechHidPpDevice(hid, id, name, hidDevice.DevicePath, hidDevice.ProductID, deviceIndex, featureIndex, rgbEffects, dryRun, online: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            hid?.Dispose();
            return new LogitechHidPpDevice(null, id, name, hidDevice.DevicePath, hidDevice.ProductID, LogitechHidPpProtocol.WiredDeviceIndex, 0, true, dryRun, online: false);
        }
    }

    public bool TryResizeZone(string zoneId, int ledCount) => false;

    public ValueTask UpdateLedsAsync(ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_dryRun || _hid is null || _featureIndex == 0 || colors.IsEmpty)
        {
            return ValueTask.CompletedTask;
        }

        lock (_transaction)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Span<byte> report = stackalloc byte[LogitechHidPpProtocol.ReportLength];
            var span = colors.Span;
            var zones = Math.Min(Zones.Count, span.Length);
            for (var zone = 0; zone < zones; zone++)
            {
                LogitechHidPpProtocol.WriteSetZoneEffect(_deviceIndex, _featureIndex, _zoneFunction, (byte)zone, span[zone], report);
                _hid.Write(report);
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
                _hid?.Dispose();
            }
        }

        return ValueTask.CompletedTask;
    }

    private static bool TryResolveFeature(HidReportChannel hid, out byte deviceIndex, out byte featureIndex, out bool rgbEffects8071)
    {
        ReadOnlySpan<byte> indexes = [LogitechHidPpProtocol.WiredDeviceIndex, LogitechHidPpProtocol.WirelessDeviceIndex];
        foreach (var index in indexes)
        {
            if (TryGetFeature(hid, index, LogitechHidPpProtocol.FeatureRgbEffects, out featureIndex, out var invalidDevice))
            {
                deviceIndex = index;
                rgbEffects8071 = true;
                return true;
            }

            if (invalidDevice)
            {
                continue;
            }

            if (TryGetFeature(hid, index, LogitechHidPpProtocol.FeatureColorLedEffects, out featureIndex, out _))
            {
                deviceIndex = index;
                rgbEffects8071 = false;
                return true;
            }
        }

        deviceIndex = 0;
        featureIndex = 0;
        rgbEffects8071 = true;
        return false;
    }

    private static bool TryGetFeature(HidReportChannel hid, byte deviceIndex, ushort featureId, out byte featureIndex, out bool invalidDeviceIndex)
    {
        featureIndex = 0;
        invalidDeviceIndex = false;
        Span<byte> request = stackalloc byte[LogitechHidPpProtocol.ReportLength];
        LogitechHidPpProtocol.WriteGetFeature(deviceIndex, featureId, request);
        var response = hid.Request(request, static buffer => buffer.Length >= 5 && buffer[0] == LogitechHidPpProtocol.LongReportId, QueryTimeoutMs);
        if (response is null)
        {
            return false;
        }

        if (LogitechHidPpProtocol.TryParseGetFeatureResponse(response, out featureIndex, out invalidDeviceIndex))
        {
            return true;
        }

        invalidDeviceIndex = response.Length > 4 && response[2] == LogitechHidPpProtocol.FeatureIndexError && response[4] == LogitechHidPpProtocol.ErrorInvalidDeviceIndex;
        return false;
    }

    private static RgbZone Zone(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Topology = LedTopology.Point,
        Leds = LedLayouts.Point(),
        IsResizable = false,
        MaxLedCount = 1,
    };

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
