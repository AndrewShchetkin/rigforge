using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.Peripherals.Logitech;

/// <summary>
/// Logitech HID++ 2.0 long report (<c>0x11</c>, 20 bytes) on the vendor collection
/// usage page <c>0xFF00</c> / usage <c>0x0002</c>. Zone effects are volatile.
/// Onboard profile flash is never written.
/// </summary>
public static class LogitechHidPpProtocol
{
    public const int VendorId = 0x046D;
    public const byte LongReportId = 0x11;
    public const int ReportLength = 20;
    public const ushort UsagePage = 0xFF00;
    public const ushort Usage = 0x0002;

    public const byte WiredDeviceIndex = 0xFF;
    public const byte WirelessDeviceIndex = 0x01;
    public const byte FeatureIndexRoot = 0x00;
    public const byte FeatureIndexError = 0xFF;
    public const byte SoftwareId = 0x0A;
    public const byte ErrorInvalidDeviceIndex = 0x8F;

    public const ushort FeatureRoot = 0x0000;
    public const ushort FeatureRgbEffects = 0x8071;
    public const ushort FeatureColorLedEffects = 0x8070;

    /// <summary>IRoot GetFeature: function 0x00 combined with software id 0x0A.</summary>
    public const byte GetFeatureFunction = 0x0A;

    /// <summary>RGB Effects (0x8071) set-zone-effect: function 0x10 combined with software id 0x0A.</summary>
    public const byte SetZoneEffectFunction = 0x1A;

    /// <summary>Color LED Effects (0x8070) set-effect function 0x30 combined with software id 0x0A.</summary>
    public const byte SetZoneEffectFunction8070 = 0x3A;

    public const byte EffectFixed = 0x01;

    public const byte SoftwareControl8071 = 0x5A;
    public const byte SoftwareControl8070 = 0x8A;

    public const int ZoneCount = 2;

    public static readonly IReadOnlySet<int> ProductIds = new HashSet<int>
    {
        0xC08B, // G502 HERO
        0xC07D, // G502 Proteus Spectrum
        0xC084, // G203 / G102
        0xC092, // G203 Lightsync
        0xC088, // G Pro Wireless
        0xC094, // G Pro X Superlight
        0xC08C, // G502 Lightspeed
        0xC336, // G213
        0xC339, // G Pro Keyboard
        0xC33F, // G815
        0xC342, // G915
        0x0AAA, // G733
    };

    public static void WriteGetFeature(byte deviceIndex, ushort featureId, Span<byte> report)
    {
        Require(report);
        report.Clear();
        report[0] = LongReportId;
        report[1] = deviceIndex;
        report[2] = FeatureIndexRoot;
        report[3] = GetFeatureFunction;
        report[4] = (byte)(featureId >> 8);
        report[5] = (byte)featureId;
    }

    /// <summary>
    /// Direct zone color:
    /// <c>[0x11, devIdx, featureIdx, 0x1A, zoneIdx, 0x01, R, G, B]</c>.
    /// The effect is fixed/direct and is not persisted.
    /// </summary>
    public static void WriteSetZoneEffect(byte deviceIndex, byte featureIndex, byte zoneIndex, RgbColor color, Span<byte> report)
    {
        WriteSetZoneEffect(deviceIndex, featureIndex, SetZoneEffectFunction, zoneIndex, color, report);
    }

    public static void WriteSetZoneEffect(byte deviceIndex, byte featureIndex, byte function, byte zoneIndex, RgbColor color, Span<byte> report)
    {
        Require(report);
        report.Clear();
        report[0] = LongReportId;
        report[1] = deviceIndex;
        report[2] = featureIndex;
        report[3] = function;
        report[4] = zoneIndex;
        report[5] = EffectFixed;
        report[6] = color.R;
        report[7] = color.G;
        report[8] = color.B;
    }

    /// <summary>
    /// Volatile software-control enable. The persist byte stays 0 so onboard flash is left alone.
    /// </summary>
    public static void WriteSoftwareControl(byte deviceIndex, byte featureIndex, bool rgbEffects8071, Span<byte> report)
    {
        Require(report);
        report.Clear();
        report[0] = LongReportId;
        report[1] = deviceIndex;
        report[2] = featureIndex;
        if (rgbEffects8071)
        {
            report[3] = SoftwareControl8071;
            report[4] = 0x01;
            report[5] = 0x01;
            report[6] = 0x00;
            return;
        }

        report[3] = SoftwareControl8070;
        report[4] = 0x01;
        report[5] = 0x00;
    }

    public static bool TryParseGetFeatureResponse(ReadOnlySpan<byte> report, out byte featureIndex, out bool invalidDeviceIndex)
    {
        featureIndex = 0;
        invalidDeviceIndex = false;
        if (report.Length < 5 || report[0] != LongReportId)
        {
            return false;
        }

        if (report[2] == FeatureIndexError)
        {
            invalidDeviceIndex = report[4] == ErrorInvalidDeviceIndex;
            return false;
        }

        if ((report[3] & 0x0F) != SoftwareId)
        {
            return false;
        }

        featureIndex = report[4];
        return featureIndex != 0;
    }

    private static void Require(Span<byte> report)
    {
        if (report.Length < ReportLength)
        {
            throw new ArgumentException($"A HID++ long report needs at least {ReportLength} bytes.", nameof(report));
        }
    }
}
