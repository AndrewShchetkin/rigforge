using System.Globalization;
using HidSharp;
using RigForge.Infrastructure.CrossPlatform.Hid;

namespace RigForge.Tools.HardwareScanner;

/// <summary>Prints raw input reports a device sends on its own (status streams). Read-only: nothing is written.</summary>
internal static class HidDumpCommand
{
    public static int Run(string vendor, string product, int count, int timeoutMs)
    {
        if (!TryParseHex(vendor, out var vendorId) || !TryParseHex(product, out var productId))
        {
            Console.Error.WriteLine("Vendor and product ids must be hexadecimal, e.g. --vid 1e71 --pid 2022.");
            return 1;
        }

        var device = DeviceList.Local.GetHidDevices(vendorId, productId)
            .OrderByDescending(d => d.GetMaxInputReportLength())
            .FirstOrDefault();
        if (device is null)
        {
            Console.Error.WriteLine($"No HID device {vendorId:X4}:{productId:X4} found.");
            return 1;
        }

        Console.WriteLine($"{device.SafeProductName()} ({device.DevicePath}), input reports up to {device.GetMaxInputReportLength()} bytes");
        using var stream = device.Open();
        stream.ReadTimeout = timeoutMs;
        var buffer = new byte[device.GetMaxInputReportLength()];

        for (var i = 0; i < count; i++)
        {
            try
            {
                var length = stream.Read(buffer, 0, buffer.Length);
                var significant = buffer.AsSpan(0, length).LastIndexOfAnyExcept((byte)0) + 1;
                Console.WriteLine($"#{i + 1,-3} {length,4} bytes: {Convert.ToHexString(buffer, 0, Math.Max(significant, Math.Min(length, 2)))}");
            }
            catch (TimeoutException)
            {
                Console.WriteLine($"#{i + 1,-3} no report within {timeoutMs} ms");
            }
        }

        return 0;
    }

    private static bool TryParseHex(string value, out int result) =>
        int.TryParse(value.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
}
