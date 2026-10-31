using LibUsbDotNet;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;
using Microsoft.Extensions.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace RigForge.Infrastructure.CrossPlatform.Usb;

/// <summary>libusb-1.0 backend (needs libusb-1.0.so.0 on Linux; libusb-1.0.dll next to the binary on Windows).</summary>
internal sealed partial class LibUsbBulkTransportFactory(ILogger<LibUsbBulkTransportFactory> logger) : IUsbBulkTransportFactory
{
    private const int WriteTimeoutMs = 2000;

    public string BackendName => "libusb";

    public IUsbBulkTransport? TryOpen(int vendorId, int productId, string? serialNumber, byte endpointAddress)
    {
        UsbContext? context = null;
        try
        {
            context = new UsbContext();
            var candidates = context.List().Where(d => d.VendorId == vendorId && d.ProductId == productId).ToList();

            foreach (var device in candidates)
            {
                if (!device.TryOpen())
                {
                    LogCannotOpen(vendorId, productId);
                    continue;
                }

                if (candidates.Count > 1 && !string.IsNullOrEmpty(serialNumber)
                    && !string.Equals(device.Info.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase))
                {
                    device.Close();
                    continue;
                }

                var interfaceNumber = FindInterface(device, endpointAddress);
                if (interfaceNumber is null)
                {
                    device.Close();
                    continue;
                }

                device.ClaimInterface(interfaceNumber.Value);
                var writer = device.OpenEndpointWriter((WriteEndpointID)endpointAddress);
                return new LibUsbBulkTransport(context, device, interfaceNumber.Value, writer);
            }

            LogNotFound(vendorId, productId, endpointAddress);
        }
        catch (DllNotFoundException)
        {
            LogLibUsbMissing();
        }
        catch (Exception ex)
        {
            LogOpenFailed(ex, vendorId, productId);
        }

        context?.Dispose();
        return null;
    }

    private static int? FindInterface(IUsbDevice device, byte endpointAddress)
    {
        foreach (var config in device.Configs)
        {
            foreach (var usbInterface in config.Interfaces)
            {
                if (usbInterface.Endpoints.Any(e => e.EndpointAddress == endpointAddress))
                {
                    return usbInterface.Number;
                }
            }
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "libusb cannot open {Vid:X4}:{Pid:X4}; check permissions (udev rules on Linux)")]
    private partial void LogCannotOpen(int vid, int pid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No USB interface with endpoint 0x{Endpoint:X2} found on {Vid:X4}:{Pid:X4}")]
    private partial void LogNotFound(int vid, int pid, byte endpoint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "libusb-1.0 is not installed; AIO screen output is disabled")]
    private partial void LogLibUsbMissing();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Opening the bulk endpoint of {Vid:X4}:{Pid:X4} failed")]
    private partial void LogOpenFailed(Exception exception, int vid, int pid);

    private sealed class LibUsbBulkTransport(UsbContext context, IUsbDevice device, int interfaceNumber, UsbEndpointWriter writer) : IUsbBulkTransport
    {
        private readonly Lock _gate = new();

        public void Write(ReadOnlySpan<byte> data)
        {
            var buffer = data.ToArray();
            lock (_gate)
            {
                var error = writer.Write(buffer, 0, buffer.Length, WriteTimeoutMs, out var written);
                if (error != Error.Success || written != buffer.Length)
                {
                    throw new IOException($"USB bulk write failed: {error} ({written}/{buffer.Length} bytes).");
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                try
                {
                    device.ReleaseInterface(interfaceNumber);
                }
                catch (UsbException)
                {
                }

                device.Close();
                context.Dispose();
            }
        }
    }
}
