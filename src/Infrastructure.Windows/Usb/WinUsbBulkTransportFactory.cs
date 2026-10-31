using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using RigForge.Infrastructure.CrossPlatform.Usb;

namespace RigForge.Infrastructure.Windows.Usb;

/// <summary>
/// Bulk transport over the native WinUSB API. The Kraken exposes its screen interface WinUSB-bound
/// (the same path liquidctl takes on Windows), so no libusb DLL or driver installation is needed.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class WinUsbBulkTransportFactory(ILogger<WinUsbBulkTransportFactory> logger) : IUsbBulkTransportFactory
{
    private const uint GenericReadWrite = 0xC0000000;
    private const uint FileShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint PipeTransferTimeout = 0x03;
    private const uint WriteTimeoutMs = 2000;

    public string BackendName => "WinUSB";

    public IUsbBulkTransport? TryOpen(int vendorId, int productId, string? serialNumber, byte endpointAddress)
    {
        var hardwareId = $"VID_{vendorId:X4}&PID_{productId:X4}";
        var paths = WinUsbDeviceFinder.FindInterfacePaths(hardwareId, serialNumber);

        foreach (var path in paths)
        {
            var file = CreateFileW(path, GenericReadWrite, FileShareReadWrite, 0, OpenExisting, FileAttributeNormal | FileFlagOverlapped, 0);
            if (file.IsInvalid)
            {
                LogOpenFailed(path, Marshal.GetLastPInvokeError());
                file.Dispose();
                continue;
            }

            if (!WinUsb_Initialize(file, out var handle))
            {
                file.Dispose();
                continue;
            }

            if (!HasPipe(handle, endpointAddress))
            {
                WinUsb_Free(handle);
                file.Dispose();
                continue;
            }

            var timeout = WriteTimeoutMs;
            WinUsb_SetPipePolicy(handle, endpointAddress, PipeTransferTimeout, sizeof(uint), &timeout);
            return new WinUsbBulkTransport(file, handle, endpointAddress);
        }

        LogNotFound(hardwareId, paths.Count);
        return null;
    }

    private static bool HasPipe(nint handle, byte endpointAddress)
    {
        if (!WinUsb_QueryInterfaceSettings(handle, 0, out var descriptor))
        {
            return false;
        }

        for (byte i = 0; i < descriptor.NumEndpoints; i++)
        {
            if (WinUsb_QueryPipe(handle, 0, i, out var pipe) && pipe.PipeId == endpointAddress)
            {
                return true;
            }
        }

        return false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Opening WinUSB interface {Path} failed (Win32 error {Error}); is NZXT CAM running?")]
    private partial void LogOpenFailed(string path, int error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No usable WinUSB bulk interface for {HardwareId} ({Candidates} candidate path(s)); the screen stays on the firmware view")]
    private partial void LogNotFound(string hardwareId, int candidates);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string fileName, uint access, uint share, nint security, uint creation, uint flags, nint template);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinUsb_Initialize(SafeFileHandle device, out nint interfaceHandle);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinUsb_Free(nint interfaceHandle);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinUsb_WritePipe(nint interfaceHandle, byte pipeId, byte* buffer, uint length, out uint transferred, nint overlapped);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinUsb_SetPipePolicy(nint interfaceHandle, byte pipeId, uint policyType, uint valueLength, void* value);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinUsb_QueryInterfaceSettings(nint interfaceHandle, byte alternateInterfaceNumber, out UsbInterfaceDescriptor descriptor);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinUsb_QueryPipe(nint interfaceHandle, byte alternateInterfaceNumber, byte pipeIndex, out PipeInformation pipeInformation);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct UsbInterfaceDescriptor
    {
        public byte Length;
        public byte DescriptorType;
        public byte InterfaceNumber;
        public byte AlternateSetting;
        public byte NumEndpoints;
        public byte InterfaceClass;
        public byte InterfaceSubClass;
        public byte InterfaceProtocol;
        public byte Interface;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PipeInformation
    {
        public int PipeType;
        public byte PipeId;
        public ushort MaximumPacketSize;
        public byte Interval;
    }

    private sealed class WinUsbBulkTransport(SafeFileHandle file, nint handle, byte endpointAddress) : IUsbBulkTransport
    {
        private readonly Lock _gate = new();
        private bool _disposed;

        public void Write(ReadOnlySpan<byte> data)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                fixed (byte* buffer = data)
                {
                    if (!WinUsb_WritePipe(handle, endpointAddress, buffer, (uint)data.Length, out var written, 0) || written != data.Length)
                    {
                        throw new IOException($"WinUSB bulk write failed (Win32 error {Marshal.GetLastPInvokeError()}, {written}/{data.Length} bytes).");
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                WinUsb_Free(handle);
                file.Dispose();
            }
        }
    }
}
