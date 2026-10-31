namespace RigForge.Infrastructure.CrossPlatform.Usb;

/// <summary>An opened, claimed bulk OUT endpoint (used for AIO screen frames).</summary>
public interface IUsbBulkTransport : IDisposable
{
    void Write(ReadOnlySpan<byte> data);
}

/// <summary>
/// Opens bulk endpoints. Windows uses the native WinUSB API (the Kraken's vendor interface is WinUSB-bound,
/// no extra DLL needed); Linux uses libusb-1.0 through LibUsbDotNet.
/// </summary>
public interface IUsbBulkTransportFactory
{
    string BackendName { get; }

    /// <summary>Returns null (and logs why) when the device or its endpoint cannot be opened.</summary>
    IUsbBulkTransport? TryOpen(int vendorId, int productId, string? serialNumber, byte endpointAddress);
}
