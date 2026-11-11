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

/// <summary>Identity of a GPU I2C adapter that can be listed without opening it or touching its registers.</summary>
public readonly record struct GpuBusDescriptor(string BusId, string AdapterName, string Location, GpuPciIdentity PciId);

public interface IGpuI2cBusFactory
{
    string BackendName { get; }

    bool IsAvailable { get; }

    string DiagnosticStatus { get; }

    IReadOnlyList<IGpuI2cBus> OpenBuses();

    /// <summary>
    /// Lists the usable GPU adapters without opening them, so already-managed or previously rejected buses are never
    /// reopened on a rescan. Factories that cannot enumerate return an empty list and callers fall back to
    /// <see cref="OpenBuses"/>.
    /// </summary>
    IReadOnlyList<GpuBusDescriptor> EnumerateAdapters() => [];

    /// <summary>Opens one adapter previously returned by <see cref="EnumerateAdapters"/>; <c>null</c> when it is gone or cannot be opened.</summary>
    IGpuI2cBus? TryOpenBus(string busId) => null;
}

internal sealed class NullGpuI2cBusFactory : IGpuI2cBusFactory
{
    public string BackendName => "None";

    public bool IsAvailable => false;

    public string DiagnosticStatus => "No platform GPU I2C backend is registered.";

    public IReadOnlyList<IGpuI2cBus> OpenBuses() => [];
}
