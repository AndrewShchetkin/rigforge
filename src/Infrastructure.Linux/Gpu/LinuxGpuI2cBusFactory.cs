using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using RigForge.Infrastructure.CrossPlatform.Gpu;

namespace RigForge.Infrastructure.Linux.Gpu;

/// <summary>One internal GPU RGB adapter found under a discrete NVIDIA display controller in sysfs.</summary>
internal sealed record LinuxGpuI2cAdapter(string BusName, string AdapterName, string PciAddress, GpuPciIdentity PciId);

/// <summary>
/// Opens the GPU's own <c>/dev/i2c-*</c> adapter, scoped by PCI identity: only adapters that sysfs places under a
/// discrete NVIDIA display controller (<c>10de</c>, class <c>0x03xxxx</c>) and whose name is the internal port-1 adapter are
/// used. Monitor DDC / DisplayPort aux adapters and chipset SMBus adapters are never opened. Every transfer is gated by
/// <see cref="GpuI2cAddressFirewall"/>.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class LinuxGpuI2cBusFactory : IGpuI2cBusFactory
{
    private const int ReadOk = 4;
    private const int WriteOk = 2;
    private const int DisplayControllerBaseClass = 0x03;

    private static readonly string[] RejectedNameParts = ["DDC", "DP-", "HDMI", "AUX", "dmdc"];

    private readonly string _sysfsRoot;
    private readonly string _devRoot;

    public LinuxGpuI2cBusFactory()
        : this("/sys", "/dev")
    {
    }

    internal LinuxGpuI2cBusFactory(string sysfsRoot, string devRoot)
    {
        _sysfsRoot = sysfsRoot;
        _devRoot = devRoot;
    }

    public string BackendName => "i2c-dev";

    public bool IsAvailable => DiscoverAdapters().Any(a => CanReadWrite(DevicePath(a)));

    public string DiagnosticStatus
    {
        get
        {
            var adapters = DiscoverAdapters();
            if (adapters.Count == 0)
            {
                return "No internal NVIDIA GPU I2C adapter found in sysfs (load i2c-dev and the NVIDIA driver; run as root or install the RigForge udev rules).";
            }

            return adapters.Any(a => CanReadWrite(DevicePath(a)))
                ? $"Linux i2c-dev GPU adapters available ({adapters.Count} internal NVIDIA adapter(s))."
                : "Internal NVIDIA GPU I2C adapters were found, but no /dev/i2c-* node is writable (load i2c-dev, install the RigForge udev rules or run as root).";
        }
    }

    public IReadOnlyList<IGpuI2cBus> OpenBuses()
    {
        var buses = new List<IGpuI2cBus>();
        foreach (var adapter in DiscoverAdapters())
        {
            var devicePath = DevicePath(adapter);
            if (!CanReadWrite(devicePath))
            {
                continue;
            }

            if (LinuxGpuI2cBus.TryOpen(adapter, devicePath) is { } bus)
            {
                buses.Add(bus);
            }
        }

        return buses;
    }

    /// <summary>
    /// True only for the NVIDIA driver's internal RGB adapter (<c>NVIDIA i2c adapter 1 at B:D.F</c>). Ports 0, 2, 3, ... are
    /// monitor DDC buses and are never accepted; anything that does not name its port is rejected as well.
    /// </summary>
    internal static bool IsInternalNvidiaI2cAdapter(string? adapterName)
    {
        if (!IsInternalGpuRgbAdapter(adapterName) || adapterName is null)
        {
            return false;
        }

        const string Prefix = "NVIDIA i2c adapter ";
        if (!adapterName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = adapterName.AsSpan(Prefix.Length);
        var digits = 0;
        while (digits < rest.Length && char.IsAsciiDigit(rest[digits]))
        {
            digits++;
        }

        if (digits == 0 || (digits < rest.Length && rest[digits] != ' '))
        {
            return false;
        }

        return int.TryParse(rest[..digits], NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port == 1;
    }

    /// <summary>Name filter shared by every GPU vendor: rejects DDC, DisplayPort, HDMI and aux channel adapters.</summary>
    internal static bool IsInternalGpuRgbAdapter(string? adapterName)
    {
        if (string.IsNullOrWhiteSpace(adapterName))
        {
            return false;
        }

        foreach (var part in RejectedNameParts)
        {
            if (adapterName.Contains(part, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    internal IReadOnlyList<LinuxGpuI2cAdapter> DiscoverAdapters()
    {
        var result = new List<LinuxGpuI2cAdapter>();
        var pciRoot = Path.Combine(_sysfsRoot, "bus", "pci", "devices");
        if (!Directory.Exists(pciRoot))
        {
            return result;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pciDir in SafeEnumerateDirectories(pciRoot, "*"))
        {
            if (!TryReadNvidiaDisplayController(pciDir, out var pciId))
            {
                continue;
            }

            var pciAddress = Path.GetFileName(pciDir);
            foreach (var adapterDir in EnumerateAdapterDirectories(pciDir))
            {
                var busName = Path.GetFileName(adapterDir);
                if (!seen.Contains(busName) && TryReadName(adapterDir) is { } name && IsInternalNvidiaI2cAdapter(name))
                {
                    seen.Add(busName);
                    result.Add(new LinuxGpuI2cAdapter(busName, name, pciAddress, pciId));
                }
            }
        }

        return result;
    }

    private IEnumerable<string> EnumerateAdapterDirectories(string pciDir)
    {
        foreach (var dir in SafeEnumerateDirectories(pciDir, "i2c-*"))
        {
            yield return dir;
        }

        var drmRoot = Path.Combine(pciDir, "drm");
        foreach (var card in SafeEnumerateDirectories(drmRoot, "card*"))
        {
            foreach (var dir in SafeEnumerateDirectories(card, "i2c-*"))
            {
                yield return dir;
            }
        }

        var classRoot = Path.Combine(_sysfsRoot, "class", "i2c-adapter");
        var realPci = ResolveLink(pciDir);
        foreach (var dir in SafeEnumerateDirectories(classRoot, "i2c-*"))
        {
            var parent = ResolveLink(Path.Combine(dir, "device"));
            if (string.Equals(parent, realPci, StringComparison.Ordinal))
            {
                yield return dir;
            }
        }
    }

    private static bool TryReadNvidiaDisplayController(string pciDir, out GpuPciIdentity pciId)
    {
        pciId = default;
        if (!TryReadHex(Path.Combine(pciDir, "vendor"), out var vendor)
            || vendor != GpuPciIdentity.NvidiaVendorId
            || !TryReadHex(Path.Combine(pciDir, "class"), out var pciClass)
            || (pciClass >> 16) != DisplayControllerBaseClass
            || !TryReadHex(Path.Combine(pciDir, "device"), out var device)
            || !TryReadHex(Path.Combine(pciDir, "subsystem_vendor"), out var subVendor)
            || !TryReadHex(Path.Combine(pciDir, "subsystem_device"), out var subDevice))
        {
            return false;
        }

        pciId = new GpuPciIdentity((ushort)vendor, (ushort)device, (ushort)subVendor, (ushort)subDevice);
        return true;
    }

    private static bool TryReadHex(string path, out int value)
    {
        value = 0;
        try
        {
            var text = File.ReadAllText(path).Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                text = text[2..];
            }

            return int.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? TryReadName(string adapterDir)
    {
        try
        {
            return File.ReadAllText(Path.Combine(adapterDir, "name")).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path, string pattern)
    {
        try
        {
            return Directory.Exists(path) ? Directory.GetDirectories(path, pattern) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string ResolveLink(string path)
    {
        try
        {
            return new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Path.GetFullPath(path);
        }
    }

    private string DevicePath(LinuxGpuI2cAdapter adapter) => Path.Combine(_devRoot, adapter.BusName);

    private static bool CanReadWrite(string path) =>
        OperatingSystem.IsLinux() && File.Exists(path) && Access(path, ReadOk | WriteOk) == 0;

    [LibraryImport("libc", EntryPoint = "access", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Access(string pathname, int mode);
}

/// <summary>
/// One GPU's <c>/dev/i2c-N</c>. <see cref="AcquireBusLock"/> returns a single pre-allocated scope: re-entrant on the calling
/// thread (<see cref="Monitor"/>) with the cross-process <c>flock(LOCK_EX)</c> taken on the device descriptor on the outermost
/// entry only. A failed <c>flock</c> refuses the transfer; no <c>ioctl</c> ever runs without it.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class LinuxGpuI2cBus : IGpuI2cBus
{
    private const ulong I2cRdwr = 0x0707;
    private const ushort I2cMessageRead = 0x0001;
    private const int MaxTransferBytes = 256;
    private const int LockExclusive = 2;
    private const int LockUnlock = 8;
    private const int Eintr = 4;

    private readonly SafeFileHandle _handle;
    private readonly object _sync = new();
    private readonly LockScope _scope;
    private readonly bool _useCrossProcessLock;
    private int _depth;
    private bool _deviceLockHeld;
    private bool _disposed;

    private LinuxGpuI2cBus(SafeFileHandle handle, string busId, string adapterName, string location, GpuPciIdentity pciId, bool useCrossProcessLock)
    {
        _handle = handle;
        BusId = busId;
        AdapterName = adapterName;
        Location = location;
        PciId = pciId;
        _useCrossProcessLock = useCrossProcessLock;
        _scope = new LockScope(this);
    }

    public string BusId { get; }

    public string AdapterName { get; }

    public string Location { get; }

    public GpuPciIdentity PciId { get; }

    internal static LinuxGpuI2cBus? TryOpen(LinuxGpuI2cAdapter adapter, string devicePath)
    {
        try
        {
            var handle = File.OpenHandle(devicePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return new LinuxGpuI2cBus(
                handle,
                $"pci-{adapter.PciAddress}-{adapter.BusName}",
                adapter.AdapterName,
                $"{devicePath} ({adapter.AdapterName}, PCI {adapter.PciAddress})",
                adapter.PciId,
                useCrossProcessLock: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Wraps an existing handle so firewall and lock behavior can be tested without <c>/dev/i2c-*</c>.</summary>
    internal static LinuxGpuI2cBus CreateForTests(SafeFileHandle handle, GpuPciIdentity pciId, bool useCrossProcessLock = false)
    {
        ArgumentNullException.ThrowIfNull(handle);
        return new LinuxGpuI2cBus(handle, "i2c-test", "NVIDIA i2c adapter 1 at test", "test", pciId, useCrossProcessLock);
    }

    public IDisposable AcquireBusLock()
    {
        if (!TryEnterLock())
        {
            throw new IOException($"Refusing unlocked GPU I2C transfers on {Location}: flock(LOCK_EX) on the i2c device failed.");
        }

        return _scope;
    }

    public bool TryWriteRegisterBlock(byte slaveAddress, ReadOnlySpan<byte> registerAddress, ReadOnlySpan<byte> data)
    {
        GpuI2cAddressFirewall.ValidateSlaveAddress(slaveAddress);
        var length = registerAddress.Length + data.Length;
        if (data.Length == 0 || length > MaxTransferBytes)
        {
            return false;
        }

        Span<byte> combined = stackalloc byte[length];
        registerAddress.CopyTo(combined);
        data.CopyTo(combined[registerAddress.Length..]);
        return TryWriteRaw(slaveAddress, combined);
    }

    public unsafe bool TryReadRegisterBlock(byte slaveAddress, ReadOnlySpan<byte> registerAddress, Span<byte> destination)
    {
        GpuI2cAddressFirewall.ValidateSlaveAddress(slaveAddress);
        if (registerAddress.Length is 0 or > MaxTransferBytes || destination.Length is 0 or > MaxTransferBytes)
        {
            return false;
        }

        if (!TryEnterLock())
        {
            return false;
        }

        try
        {
            if (TransferBlocked())
            {
                return false;
            }

            fixed (byte* pRegister = registerAddress)
            fixed (byte* pData = destination)
            {
                var messages = stackalloc I2cMessage[2];
                messages[0] = new I2cMessage { Address = slaveAddress, Flags = 0, Length = (ushort)registerAddress.Length, Buffer = pRegister };
                messages[1] = new I2cMessage { Address = slaveAddress, Flags = I2cMessageRead, Length = (ushort)destination.Length, Buffer = pData };

                if (Transfer(messages, 2))
                {
                    return true;
                }

                // Controllers without repeated-start support: separate write, then read.
                return Transfer(messages, 1) && Transfer(messages + 1, 1);
            }
        }
        finally
        {
            ExitLock();
        }
    }

    public unsafe bool TryWriteRaw(byte slaveAddress, ReadOnlySpan<byte> payload)
    {
        GpuI2cAddressFirewall.ValidateSlaveAddress(slaveAddress);
        if (payload.Length is 0 or > MaxTransferBytes || !TryEnterLock())
        {
            return false;
        }

        try
        {
            if (TransferBlocked())
            {
                return false;
            }

            fixed (byte* pData = payload)
            {
                var message = new I2cMessage { Address = slaveAddress, Flags = 0, Length = (ushort)payload.Length, Buffer = pData };
                return Transfer(&message, 1);
            }
        }
        finally
        {
            ExitLock();
        }
    }

    public unsafe bool TryReadRaw(byte slaveAddress, Span<byte> destination)
    {
        GpuI2cAddressFirewall.ValidateSlaveAddress(slaveAddress);
        if (destination.Length is 0 or > MaxTransferBytes || !TryEnterLock())
        {
            return false;
        }

        try
        {
            if (TransferBlocked())
            {
                return false;
            }

            fixed (byte* pData = destination)
            {
                var message = new I2cMessage { Address = slaveAddress, Flags = I2cMessageRead, Length = (ushort)destination.Length, Buffer = pData };
                return Transfer(&message, 1);
            }
        }
        finally
        {
            ExitLock();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseFileLock();
            _handle.Dispose();
        }
    }

    private unsafe bool Transfer(I2cMessage* messages, uint count)
    {
        var data = new I2cRdwrIoctlData { Messages = messages, MessageCount = count };
        return IoctlRdwr(_handle, I2cRdwr, &data) >= 0;
    }

    /// <summary>True when an ioctl would run without the required device flock, or the bus is unusable.</summary>
    private bool TransferBlocked() =>
        _disposed || !OperatingSystem.IsLinux() || (_useCrossProcessLock && !_deviceLockHeld);

    private bool TryEnterLock()
    {
        Monitor.Enter(_sync);
        if (_depth == 0 && !TryAcquireFileLock())
        {
            Monitor.Exit(_sync);
            return false;
        }

        _depth++;
        return true;
    }

    private void ExitLock()
    {
        try
        {
            if (_depth > 0)
            {
                _depth--;
            }

            if (_depth == 0)
            {
                ReleaseFileLock();
            }
        }
        finally
        {
            Monitor.Exit(_sync);
        }
    }

    private bool TryAcquireFileLock()
    {
        if (!_useCrossProcessLock || _deviceLockHeld)
        {
            return true;
        }

        if (_disposed || _handle.IsClosed || _handle.IsInvalid || !OperatingSystem.IsLinux())
        {
            return false;
        }

        var added = false;
        try
        {
            _handle.DangerousAddRef(ref added);
            var fd = _handle.DangerousGetHandle().ToInt32();
            if (fd < 0 || !WaitForFlock(fd, LockExclusive))
            {
                return false;
            }

            _deviceLockHeld = true;
            return true;
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or OverflowException)
        {
            return false;
        }
        finally
        {
            if (added)
            {
                _handle.DangerousRelease();
            }
        }
    }

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
            // Closing the descriptor drops the flock when LOCK_UN can no longer be issued.
        }
        finally
        {
            if (added)
            {
                _handle.DangerousRelease();
            }
        }
    }

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

    private sealed class LockScope(LinuxGpuI2cBus owner) : IDisposable
    {
        public void Dispose() => owner.ExitLock();
    }

    /// <summary><c>struct i2c_msg</c> from <c>linux/i2c.h</c>; <c>addr</c> is the plain 7-bit address.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct I2cMessage
    {
        public ushort Address;
        public ushort Flags;
        public ushort Length;
        public byte* Buffer;
    }

    /// <summary><c>struct i2c_rdwr_ioctl_data</c> from <c>linux/i2c-dev.h</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct I2cRdwrIoctlData
    {
        public I2cMessage* Messages;
        public uint MessageCount;
    }

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static unsafe partial int IoctlRdwr(SafeFileHandle fd, ulong request, I2cRdwrIoctlData* arg);

    [LibraryImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static partial int Flock(int fd, int operation);
}
