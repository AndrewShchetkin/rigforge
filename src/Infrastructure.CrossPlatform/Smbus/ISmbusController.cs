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

    /// <summary>
    /// True when the module is an ENE RGB DRAM family this driver may probe
    /// (G.Skill Trident Z / Royal / Neo, Crucial Ballistix RGB, TeamGroup T-Force Delta, ASUS Aura DRAM).
    /// Part-number text only — no SMBus traffic.
    /// </summary>
    public bool IsEneCompatibleDram
    {
        get
        {
            var part = PartNumber.Trim();
            if (part.Length == 0)
            {
                return false;
            }

            // F4-*C / GTZ / GTZN / GTZR and F5-*J / TZ5R / TZ5NR. "GTZ" covers GTZN/GTZR; "TZ5" covers TZ5R/TZ5NR.
            if (part.Contains("GTZ", StringComparison.OrdinalIgnoreCase)
                || part.Contains("TZ5", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (part.StartsWith("F4-", StringComparison.OrdinalIgnoreCase)
                && part.Contains('C')
                && part.Contains("RGB", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (part.StartsWith("F5-", StringComparison.OrdinalIgnoreCase)
                && (part.Contains('J') || part.Contains("TZ5R", StringComparison.OrdinalIgnoreCase) || part.Contains("TZ5NR", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (part.StartsWith("BL", StringComparison.OrdinalIgnoreCase)
                && part.Contains("RGB", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if ((part.StartsWith("TF", StringComparison.OrdinalIgnoreCase) && part.Contains("D4", StringComparison.OrdinalIgnoreCase))
                || (part.StartsWith("FF", StringComparison.OrdinalIgnoreCase) && part.Contains("D5", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return Manufacturer.Contains("ASUS", StringComparison.OrdinalIgnoreCase)
                && (part.Contains("AURA", StringComparison.OrdinalIgnoreCase) || part.Contains("RGB", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// True when the module is Corsair Vengeance / Dominator RGB DRAM (CMH, CMW, CMT, CMP, or a DOMINATOR part).
    /// </summary>
    public bool IsCorsairDram
    {
        get
        {
            var part = PartNumber.Trim();
            return part.StartsWith("CMH", StringComparison.OrdinalIgnoreCase)
                || part.StartsWith("CMW", StringComparison.OrdinalIgnoreCase)
                || part.StartsWith("CMT", StringComparison.OrdinalIgnoreCase)
                || part.StartsWith("CMP", StringComparison.OrdinalIgnoreCase)
                || part.Contains("DOMINATOR", StringComparison.OrdinalIgnoreCase);
        }
    }
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

    /// <summary>
    /// False when SMBIOS/DMI could not be read because the OS denied access and no sysfs SPD, udev, or EDAC
    /// identity was available. Diagnostic only: with <c>RequireSmbiosMatch</c> left at its default, DRAM probes
    /// still refuse to open SMBus until a module is identified.
    /// </summary>
    bool CanReadSmbios => true;

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
