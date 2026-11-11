namespace RigForge.Infrastructure.CrossPlatform.Gpu;

public readonly record struct GpuPciIdentity(
    ushort VendorId,
    ushort DeviceId,
    ushort SubVendorId,
    ushort SubDeviceId)
{
    public const ushort NvidiaVendorId = 0x10DE;
    public const ushort AsusSubVendorId = 0x1043;
    public const ushort GigabyteSubVendorId = 0x1458;
    public const ushort MsiSubVendorId = 0x1462;
}

/// <summary>
/// A GPU's own I2C adapter (NVAPI / NVIDIA I2C on Windows, i2c-dev on Linux). Deliberately separate from the
/// motherboard <c>ISmbusController</c> so GPU RGB writes can never reach DRAM SPD / PMIC addresses.
/// </summary>
public interface IGpuI2cBus : IDisposable
{
    string BusId { get; }

    string AdapterName { get; }

    string Location { get; }

    GpuPciIdentity PciId { get; }

    IDisposable AcquireBusLock();

    bool TryWriteRegisterBlock(byte slaveAddress, ReadOnlySpan<byte> registerAddress, ReadOnlySpan<byte> data);

    bool TryReadRegisterBlock(byte slaveAddress, ReadOnlySpan<byte> registerAddress, Span<byte> destination);

    bool TryWriteRaw(byte slaveAddress, ReadOnlySpan<byte> payload);

    bool TryReadRaw(byte slaveAddress, Span<byte> destination);
}

public interface IGpuI2cBusFactory
{
    string BackendName { get; }

    bool IsAvailable { get; }

    string DiagnosticStatus { get; }

    IReadOnlyList<IGpuI2cBus> OpenBuses();
}

internal sealed class NullGpuI2cBusFactory : IGpuI2cBusFactory
{
    public string BackendName => "None";

    public bool IsAvailable => false;

    public string DiagnosticStatus => "No platform GPU I2C backend is registered.";

    public IReadOnlyList<IGpuI2cBus> OpenBuses() => [];
}
