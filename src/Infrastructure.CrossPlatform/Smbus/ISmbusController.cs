namespace RigForge.Infrastructure.CrossPlatform.Smbus;

/// <summary>
/// Read-only memory module descriptor from the OS SMBIOS Type 17 table.
/// Read from system memory without touching the SMBus hardware.
/// </summary>
public sealed record SmbiosMemoryStick(
    string BankLocator,
    string DeviceLocator,
    string Manufacturer,
    string PartNumber,
    string SerialNumber,
    int CapacityMegabytes,
    int SpeedMegaTransfers)
{
    /// <summary>
    /// True when the module is a Kingston FURY DDR5 stick (part numbers start with "KF5", e.g. KF556C40BBAK2-64).
    /// </summary>
    public bool IsKingstonFuryDdr5 =>
        PartNumber.Trim().StartsWith("KF5", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Low-level SMBus controller port restricted to byte-data writes and word-data reads.
/// Implementations must enforce <see cref="SmbusAddressFirewall"/> before issuing bus transactions.
/// </summary>
public interface ISmbusController : IDisposable
{
    string BusName { get; }

    string Location { get; }

    IDisposable AcquireBusLock();

    bool TryWriteByteData(byte address, byte register, byte value);

    bool TryReadWordData(byte address, byte register, out ushort value);
}

/// <summary>
/// Opens motherboard SMBus controllers (PawnIO on Windows, /dev/i2c-* on Linux) and reads SMBIOS memory entries.
/// </summary>
public interface ISmbusControllerFactory
{
    string BackendName { get; }

    bool IsAvailable { get; }

    string DiagnosticStatus { get; }

    /// <summary>Reads SMBIOS Type 17 memory devices from OS tables without generating any SMBus traffic.</summary>
    IReadOnlyList<SmbiosMemoryStick> GetInstalledMemory();

    /// <summary>Opens available primary/auxiliary chipset SMBus controllers.</summary>
    IReadOnlyList<ISmbusController> OpenControllers();
}

internal sealed class NullSmbusControllerFactory : ISmbusControllerFactory
{
    public string BackendName => "None";

    public bool IsAvailable => false;

    public string DiagnosticStatus => "No platform SMBus backend is registered.";

    public IReadOnlyList<SmbiosMemoryStick> GetInstalledMemory() => [];

    public IReadOnlyList<ISmbusController> OpenControllers() => [];
}
