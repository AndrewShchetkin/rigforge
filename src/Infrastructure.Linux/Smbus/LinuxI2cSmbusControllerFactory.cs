using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using RigForge.Infrastructure.CrossPlatform.Smbus;

namespace RigForge.Infrastructure.Linux.Smbus;

/// <summary>
/// Linux <c>/dev/i2c-*</c> SMBus backend restricted to chipset adapters and guarded by <see cref="SmbusAddressFirewall"/>.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class LinuxI2cSmbusControllerFactory : ISmbusControllerFactory
{
    private const int ReadOk = 4;
    private const int WriteOk = 2;

    private readonly string _adapterRoot;
    private readonly string _devRoot;
    private readonly string _dmiPath;
    private readonly string _spdRoot;
    private readonly string _udevDataDirectory;
    private readonly string _edacRoot;

    public LinuxI2cSmbusControllerFactory()
        : this("/sys", "/dev", "/run/udev/data")
    {
    }

    internal LinuxI2cSmbusControllerFactory(string sysfsRoot, string devRoot, string udevDataDirectory)
    {
        _adapterRoot = ResolveAdapterClass(sysfsRoot);
        _devRoot = devRoot;
        _dmiPath = Path.Combine(sysfsRoot, "firmware", "dmi", "tables", "DMI");
        _spdRoot = Path.Combine(sysfsRoot, "bus", "i2c", "devices");
        _edacRoot = Path.Combine(sysfsRoot, "devices", "system", "edac");
        _udevDataDirectory = udevDataDirectory;
    }

    /// <summary>
    /// Older kernels expose chipset adapters at <c>/sys/class/i2c-adapter</c>. Kernels that only
    /// register the <c>i2c-dev</c> class (the nodes behind <c>/dev/i2c-*</c>) keep the same
    /// <c>name</c> file there. Windows never uses this path; it opens SMBus through PawnIO.
    /// </summary>
    internal static string ResolveAdapterClass(string sysfsRoot)
    {
        var legacy = Path.Combine(sysfsRoot, "class", "i2c-adapter");
        if (Directory.Exists(legacy))
        {
            return legacy;
        }

        return Path.Combine(sysfsRoot, "class", "i2c-dev");
    }

    public string BackendName => "i2c-dev";

    public bool IsAvailable => FindChipsetAdapters().Count > 0;

    public bool CanReadSmbios { get; private set; } = true;

    public string DiagnosticStatus =>
        IsAvailable
            ? "Linux i2c-dev chipset adapters available."
            : "No writable /dev/i2c-* chipset adapter found (load i2c-dev and i2c-piix4/i2c-i801).";

    public IReadOnlyList<SmbiosMemoryStick> GetInstalledMemory()
    {
        var fromDmi = TryReadDmi(out var denied);
        if (fromDmi.Count > 0)
        {
            CanReadSmbios = true;
            return fromDmi;
        }

        var fallback = new List<SmbiosMemoryStick>();
        AppendSpdSysfs(fallback);
        AppendUdev(fallback);
        AppendEdac(fallback);
        if (fallback.Count > 0)
        {
            CanReadSmbios = true;
            return fallback;
        }

        // Permission denied on the DMI table, with nothing cached in sysfs, udev, or EDAC, is the non-root case.
        // A missing or empty-but-readable table is not a permission failure.
        // CanReadSmbios is diagnostic only. SmbusProbePolicy does not open the bus because DMI was unreadable.
        CanReadSmbios = !denied;
        return [];
    }

    public IReadOnlyList<ISmbusController> OpenControllers()
    {
        var result = new List<ISmbusController>();
        foreach (var (busId, adapterName) in FindChipsetAdapters())
        {
            var devPath = Path.Combine(_devRoot, busId);
            if (LinuxI2cSmbusController.TryOpen(busId, devPath, adapterName) is { } controller)
            {
                result.Add(controller);
            }
        }

        return result;
    }

    internal static IReadOnlyList<SmbiosMemoryStick> ParseSmbiosType17(ReadOnlySpan<byte> dmi) =>
        SmbiosType17Parser.Parse(dmi);

    private List<(string BusId, string Name)> FindChipsetAdapters()
    {
        var adapters = new List<(string, string)>();
        if (!Directory.Exists(_adapterRoot))
        {
            return adapters;
        }

        foreach (var dir in Directory.EnumerateDirectories(_adapterRoot, "i2c-*"))
        {
            var busId = Path.GetFileName(dir);
            var nameFile = Path.Combine(dir, "name");
            var devPath = Path.Combine(_devRoot, busId);
            if (!File.Exists(nameFile) || !CanReadWrite(devPath))
            {
                continue;
            }

            string adapterName;
            try
            {
                adapterName = File.ReadAllText(nameFile).Trim();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (LinuxChipsetSmbus.IsAdapter(adapterName))
            {
                adapters.Add((busId, adapterName));
            }
        }

        return adapters;
    }

    private static bool CanReadWrite(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        return Access(path, ReadOk | WriteOk) == 0;
    }

    private IReadOnlyList<SmbiosMemoryStick> TryReadDmi(out bool denied)
    {
        denied = false;
        try
        {
            if (!File.Exists(_dmiPath))
            {
                return [];
            }

            var raw = File.ReadAllBytes(_dmiPath);
            if (raw.Length == 0)
            {
                return [];
            }

            return SmbiosType17Parser.Parse(raw);
        }
        catch (UnauthorizedAccessException)
        {
            denied = true;
            return [];
        }
        catch (IOException ex) when (IsAccessDenied(ex))
        {
            denied = true;
            return [];
        }
        catch (Exception ex) when (ex is IOException or System.Security.SecurityException)
        {
            return [];
        }
    }

    private static bool IsAccessDenied(IOException exception) =>
        exception.HResult == 13 || exception.Message.Contains("denied", StringComparison.OrdinalIgnoreCase);

    private void AppendSpdSysfs(List<SmbiosMemoryStick> sticks)
    {
        if (!Directory.Exists(_spdRoot))
        {
            return;
        }

        foreach (var dir in Directory.EnumerateDirectories(_spdRoot))
        {
            var name = Path.GetFileName(dir);
            if (!SpdEepromParser.IsSpdDeviceName(name))
            {
                continue;
            }

            var eeprom = Path.Combine(dir, "eeprom");
            if (!File.Exists(eeprom))
            {
                continue;
            }

            try
            {
                var bytes = File.ReadAllBytes(eeprom);
                if (SpdEepromParser.ToStick(name, bytes) is { } stick)
                {
                    sticks.Add(stick);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The kernel driver may not have cached this slot. Other slots can still be readable.
            }
        }
    }

    private void AppendUdev(List<SmbiosMemoryStick> sticks)
    {
        if (!Directory.Exists(_udevDataDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_udevDataDirectory))
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (!text.Contains("MEMORY", StringComparison.OrdinalIgnoreCase)
                && !text.Contains("DMI", StringComparison.OrdinalIgnoreCase)
                && !text.Contains("PART_NUMBER", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();
                var part = ReadUdevValue(line, "ID_PART_NUMBER")
                    ?? ReadUdevValue(line, "PART_NUMBER")
                    ?? ReadUdevValue(line, "DIMM_PART_NUMBER");
                if (part is not { Length: >= 4 } || !LooksLikeModulePart(part))
                {
                    continue;
                }

                sticks.Add(new SmbiosMemoryStick(string.Empty, Path.GetFileName(file), string.Empty, part, string.Empty, 0, 0));
            }
        }
    }

    private void AppendEdac(List<SmbiosMemoryStick> sticks)
    {
        if (!Directory.Exists(_edacRoot))
        {
            return;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(_edacRoot, "*part*", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in files)
        {
            try
            {
                var part = File.ReadAllText(file).Trim();
                if (LooksLikeModulePart(part) && !part.StartsWith("CPU_SrcID", StringComparison.OrdinalIgnoreCase))
                {
                    sticks.Add(new SmbiosMemoryStick(string.Empty, Path.GetFileName(Path.GetDirectoryName(file) ?? file), string.Empty, part, string.Empty, 0, 0));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // One DIMM directory can be unreadable without hiding the others.
            }
        }
    }

    private static string? ReadUdevValue(string line, string key)
    {
        var prefix = "E:" + key + "=";
        return line.StartsWith(prefix, StringComparison.Ordinal) ? line[prefix.Length..].Trim() : null;
    }

    private static bool LooksLikeModulePart(string part)
    {
        if (part.Length is < 4 or > 48 || part.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        var hasLetter = false;
        var hasDigit = false;
        foreach (var ch in part)
        {
            if (char.IsAsciiLetter(ch))
            {
                hasLetter = true;
            }
            else if (char.IsAsciiDigit(ch))
            {
                hasDigit = true;
            }
            else if (ch is not ('-' or '_' or '.' or '+'))
            {
                return false;
            }
        }

        return hasLetter && hasDigit;
    }

    [LibraryImport("libc", EntryPoint = "access", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Access(string pathname, int mode);
}

/// <summary>
/// One <c>/dev/i2c-N</c> SMBus file. <see cref="AcquireBusLock"/> is re-entrant on the calling thread
/// (<see cref="Monitor"/>), matching Windows <c>PawnIoSmbusController</c>. The cross-process
/// <c>flock(LOCK_EX)</c> is taken on this device descriptor, and only on the outermost entry.
/// A failed <c>flock</c> refuses the transfer: no <c>ioctl</c> runs without the lock.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class LinuxI2cSmbusController : ISmbusController
{
    private const ulong I2cSlave = 0x0703;
    private const ulong I2cSmbus = 0x0720;
    private const byte I2cSmbusWrite = 0;
    private const byte I2cSmbusRead = 1;
    private const uint I2cSmbusByteData = 2;
    private const uint I2cSmbusWordData = 3;
    private const int LockExclusive = 2;
    private const int LockUnlock = 8;
    private const int Eintr = 4;

    private readonly SafeFileHandle _handle;
    private readonly object _sync = new();
    private readonly bool _useCrossProcessLock;
    private int _depth;
    private bool _deviceLockHeld;
    private bool _disposed;

    private LinuxI2cSmbusController(SafeFileHandle handle, string busName, string location, bool useCrossProcessLock)
    {
        _handle = handle;
        BusName = busName;
        Location = location;
        _useCrossProcessLock = useCrossProcessLock;
    }

    public string BusName { get; }

    public string Location { get; }

    public static LinuxI2cSmbusController? TryOpen(string busName, string devicePath, string adapterName)
    {
        try
        {
            var handle = File.OpenHandle(devicePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return new LinuxI2cSmbusController(handle, busName, $"{devicePath} ({adapterName})", useCrossProcessLock: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Opens a controller around an existing handle so lock re-entry can be tested without <c>/dev/i2c-*</c>.</summary>
    internal static LinuxI2cSmbusController CreateForTests(SafeFileHandle handle, string busName = "i2c-test", bool useCrossProcessLock = false)
    {
        ArgumentNullException.ThrowIfNull(handle);
        return new LinuxI2cSmbusController(handle, busName, "test", useCrossProcessLock);
    }

    public IDisposable AcquireBusLock()
    {
        Monitor.Enter(_sync);
        var outer = false;
        try
        {
            if (_depth == 0)
            {
                AcquireFileLock();
                outer = true;
            }

            _depth++;
            return new BusLockScope(this, outer);
        }
        catch
        {
            Monitor.Exit(_sync);
            throw;
        }
    }

    public unsafe bool TryWriteByteData(byte address, byte register, byte value)
    {
        SmbusAddressFirewall.ValidateAddress(address);
        Monitor.Enter(_sync);
        var acquiredFile = false;
        try
        {
            if (_depth == 0)
            {
                if (!TryAcquireDeviceLock())
                {
                    return false;
                }

                acquiredFile = true;
            }

            if (TransferBlocked())
            {
                return false;
            }

            if (IoctlAddress(_handle, I2cSlave, address) < 0)
            {
                return false;
            }

            SmbusData data = default;
            data.Byte = value;
            var args = new SmbusIoctlData
            {
                ReadWrite = I2cSmbusWrite,
                Command = register,
                Size = I2cSmbusByteData,
                Data = &data,
            };

            return IoctlSmbus(_handle, I2cSmbus, &args) >= 0;
        }
        finally
        {
            if (acquiredFile)
            {
                ReleaseFileLock();
            }

            Monitor.Exit(_sync);
        }
    }

    public unsafe bool TryReadWordData(byte address, byte register, out ushort value)
    {
        SmbusAddressFirewall.ValidateAddress(address);
        value = 0;
        Monitor.Enter(_sync);
        var acquiredFile = false;
        try
        {
            if (_depth == 0)
            {
                if (!TryAcquireDeviceLock())
                {
                    return false;
                }

                acquiredFile = true;
            }

            if (TransferBlocked())
            {
                return false;
            }

            if (IoctlAddress(_handle, I2cSlave, address) < 0)
            {
                return false;
            }

            SmbusData data = default;
            var args = new SmbusIoctlData
            {
                ReadWrite = I2cSmbusRead,
                Command = register,
                Size = I2cSmbusWordData,
                Data = &data,
            };

            if (IoctlSmbus(_handle, I2cSmbus, &args) < 0)
            {
                return false;
            }

            value = data.Word;
            return true;
        }
        finally
        {
            if (acquiredFile)
            {
                ReleaseFileLock();
            }

            Monitor.Exit(_sync);
        }
    }

    public void Dispose()
    {
        Monitor.Enter(_sync);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseFileLock();
            _handle.Dispose();
        }
        finally
        {
            Monitor.Exit(_sync);
        }
    }

    private void ReleaseBusLock(bool outer)
    {
        try
        {
            if (_depth > 0)
            {
                _depth--;
            }

            if (outer)
            {
                ReleaseFileLock();
            }
        }
        finally
        {
            Monitor.Exit(_sync);
        }
    }

    /// <summary>
    /// Takes <c>flock(LOCK_EX)</c> on the already-open <c>/dev/i2c-N</c> descriptor.
    /// The lock is kernel-wide for that inode and creates no file in <c>/tmp</c>.
    /// </summary>
    private void AcquireFileLock()
    {
        if (!_useCrossProcessLock || _deviceLockHeld)
        {
            return;
        }

        if (_handle.IsClosed || _handle.IsInvalid)
        {
            throw new IOException(Refused("the i2c device is not open"));
        }

        if (!OperatingSystem.IsLinux())
        {
            throw new IOException(Refused("flock on /dev/i2c-* is only available on Linux"));
        }

        var added = false;
        try
        {
            _handle.DangerousAddRef(ref added);
            var fd = _handle.DangerousGetHandle().ToInt32();
            if (fd < 0)
            {
                throw new IOException(Refused("the i2c device is not open"));
            }

            if (!WaitForFlock(fd, LockExclusive))
            {
                throw new IOException(Refused($"flock(LOCK_EX) on /dev/i2c-* failed with errno {Marshal.GetLastPInvokeError()}"));
            }

            _deviceLockHeld = true;
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or OverflowException)
        {
            throw new IOException(Refused("the i2c device is not open"), ex);
        }
        finally
        {
            if (added)
            {
                _handle.DangerousRelease();
            }
        }
    }

    private bool TryAcquireDeviceLock()
    {
        try
        {
            AcquireFileLock();
            return !_useCrossProcessLock || _deviceLockHeld;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>True when an ioctl would run without the required device flock, or the controller is unusable.</summary>
    private bool TransferBlocked() =>
        _disposed || !OperatingSystem.IsLinux() || (_useCrossProcessLock && !_deviceLockHeld);

    private void ReleaseFileLock()
    {
        if (!_deviceLockHeld)
        {
            return;
        }

        _deviceLockHeld = false;
        if (!OperatingSystem.IsLinux() || _handle.IsClosed || _handle.IsInvalid)
        {
            return;
        }

        var added = false;
        try
        {
            _handle.DangerousAddRef(ref added);
            var fd = _handle.DangerousGetHandle().ToInt32();
            if (fd >= 0)
            {
                _ = WaitForFlock(fd, LockUnlock);
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or OverflowException)
        {
            // Closing the device descriptor drops the flock when LOCK_UN can no longer be issued.
        }
        finally
        {
            if (added)
            {
                _handle.DangerousRelease();
            }
        }
    }

    private string Refused(string reason) =>
        $"Refusing unlocked SMBus transfers on {Location}. {reason}.";

    private static bool WaitForFlock(int fd, int operation)
    {
        while (Flock(fd, operation) < 0)
        {
            if (Marshal.GetLastPInvokeError() == Eintr)
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private sealed class BusLockScope(LinuxI2cSmbusController owner, bool outer) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.ReleaseBusLock(outer);
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 34)]
    private struct SmbusData
    {
        [FieldOffset(0)]
        public byte Byte;

        [FieldOffset(0)]
        public ushort Word;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct SmbusIoctlData
    {
        public byte ReadWrite;
        public byte Command;
        public uint Size;
        public SmbusData* Data;
    }

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int IoctlAddress(SafeFileHandle fd, ulong request, nint arg);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static unsafe partial int IoctlSmbus(SafeFileHandle fd, ulong request, SmbusIoctlData* arg);

    [LibraryImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static partial int Flock(int fd, int operation);
}
