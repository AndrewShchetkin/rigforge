using System.Globalization;
using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.Gpu;

/// <summary>Stable device identifiers and shared diagnostics formatting for the GPU I2C RGB drivers.</summary>
internal static class GpuDeviceIds
{
    public const string AsusPrefix = "asus-gpu-i2c";
    public const string MsiPrefix = "msi-gpu-i2c";
    public const string GigabytePrefix = "gigabyte-gpu-i2c";

    /// <summary>Builds <c>{prefix}:{sanitized bus id}:{slave address as two lowercase hex digits}</c>.</summary>
    public static string Create(string prefix, string busId, byte slaveAddress) =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}:{Sanitize(busId)}:{slaveAddress:x2}");

    public static string? PrefixFor(DeviceProtocolFamily protocol) => protocol switch
    {
        DeviceProtocolFamily.AsusAuraGpuI2c => AsusPrefix,
        DeviceProtocolFamily.MsiMysticGpuI2c => MsiPrefix,
        DeviceProtocolFamily.GigabyteFusionGpuI2c => GigabytePrefix,
        _ => null,
    };

    public static string FormatPciId(GpuPciIdentity pci) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{pci.VendorId:X4}:{pci.DeviceId:X4} ({pci.SubVendorId:X4}:{pci.SubDeviceId:X4})");

    public static string FormatAddress(byte slaveAddress) =>
        string.Create(CultureInfo.InvariantCulture, $"0x{slaveAddress:X2}");

    /// <summary>Lower-cases letters/digits and maps every other character to '-' (trimmed), e.g. <c>NVAPI/GPU-0</c> to <c>nvapi-gpu-0</c>.</summary>
    public static string Sanitize(string value)
    {
        Span<char> buffer = value.Length <= 256 ? stackalloc char[value.Length] : new char[value.Length];
        var length = 0;
        foreach (var ch in value)
        {
            buffer[length++] = char.IsAsciiLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-';
        }

        return new string(buffer[..length]).Trim('-');
    }
}
