using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using LibreHardwareMonitor.PawnIo;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using RigForge.Infrastructure.CrossPlatform.Smbus;

namespace RigForge.Infrastructure.Windows.Smbus;

/// <summary>
/// Opens AMD PIIX4 (ports 0 and 1) and Intel i801 SMBus controllers through the signed PawnIO driver
/// using the signed modules embedded in <c>LibreHardwareMonitorLib</c>.
/// Every transfer is gated by <see cref="SmbusAddressFirewall"/> and synchronized with the global
/// <c>Access_SMBUS.HTP.Method</c> mutex shared by HWiNFO, LibreHardwareMonitor and OpenRGB.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class PawnIoSmbusControllerFactory(ILogger<PawnIoSmbusControllerFactory> logger) : ISmbusControllerFactory
{
    private const string Piix4ResourceName = "LibreHardwareMonitor.Resources.PawnIo.SmbusPIIX4.bin";
    private const string I801ResourceName = "LibreHardwareMonitor.Resources.PawnIo.SmbusI801.bin";
    private const uint RsmbSignature = 0x52534D42u; // 'RSMB'
    private const int RawSmbiosHeaderBytes = 8;

    public string BackendName => "PawnIO";

    public bool IsAvailable => Environment.IsPrivilegedProcess && PawnIo.IsInstalled;

    public string DiagnosticStatus
    {
        get
        {
            if (!PawnIo.IsInstalled)
            {
                return "PawnIO driver is not installed (install from https://pawnio.eu).";
            }

            if (!Environment.IsPrivilegedProcess)
            {
                return $"PawnIO {PawnIo.Version} is installed, but the process is not running as administrator.";
            }

            return $"PawnIO {PawnIo.Version} ready (administrator).";
        }
    }

    public unsafe IReadOnlyList<SmbiosMemoryStick> GetInstalledMemory()
    {
        try
        {
            var size = GetSystemFirmwareTable(RsmbSignature, 0, null, 0);
            if (size <= RawSmbiosHeaderBytes)
            {
                return [];
            }

            var buffer = new byte[size];
            fixed (byte* pBuffer = buffer)
            {
                var written = GetSystemFirmwareTable(RsmbSignature, 0, pBuffer, size);
                if (written <= RawSmbiosHeaderBytes || written > size)
                {
                    return [];
                }

                var declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4, 4));
                var availableLength = (int)written - RawSmbiosHeaderBytes;
                var tableLength = declaredLength > 0 && declaredLength <= (uint)availableLength
                    ? (int)declaredLength
                    : availableLength;

                return SmbiosType17Parser.Parse(buffer.AsSpan(RawSmbiosHeaderBytes, tableLength));
            }
        }
        catch (Exception ex)
        {
            LogSmbiosReadFailed(ex);
            return [];
        }
    }

    public IReadOnlyList<ISmbusController> OpenControllers()
    {
        if (!IsAvailable)
        {
            return [];
        }

        var controllers = new List<ISmbusController>(2);

        // AMD AM4/AM5 (FCH PIIX4): port 0 (0x0B00) and port 1 (0x0B20). Ports 2..4 are never touched.
        // When PIIX4 port 0 opens on an AMD system, skip loading Intel SmbusI801.bin into PawnIO.
        if (PawnIoSmbusController.TryOpenPiix4(0, logger) is { } piix4Port0)
        {
            controllers.Add(piix4Port0);
            if (PawnIoSmbusController.TryOpenPiix4(1, logger) is { } piix4Port1)
            {
                controllers.Add(piix4Port1);
            }

            return controllers;
        }

        // Intel PCH (i801)
        if (PawnIoSmbusController.TryOpenI801(logger) is { } i801)
        {
            controllers.Add(i801);
        }

        return controllers;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetSystemFirmwareTable", SetLastError = true)]
    private static unsafe partial uint GetSystemFirmwareTable(
        uint firmwareTableProviderSignature,
        uint firmwareTableId,
        byte* pFirmwareTableBuffer,
        uint bufferSize);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to read SMBIOS memory table")]
    private partial void LogSmbiosReadFailed(Exception exception);
}

[SupportedOSPlatform("windows")]
internal sealed partial class PawnIoSmbusController : ISmbusController
{
    private const string PawnIoDevicePath = @"\\?\GLOBALROOT\Device\PawnIO";
    private const uint DeviceTypePawnIo = 41394u << 16;
    private const uint IoctlLoadBinary = DeviceTypePawnIo | (0x821u << 2);
    private const uint IoctlExecuteFn = DeviceTypePawnIo | (0x841u << 2);
    private const int FunctionNameBytes = 32;

    private const ulong SmbusWrite = 0;
    private const ulong SmbusRead = 1;
    private const ulong SmbusByteData = 2;
    private const ulong SmbusWordData = 3;

    private static readonly Lock ProcessBusLock = new();

    [ThreadStatic]
    private static int _busLockDepth;

    private readonly SafeFileHandle _handle;
    private readonly Mutex? _sharedMutex;
    private bool _disposed;

    private PawnIoSmbusController(SafeFileHandle handle, string busName, string location)
    {
        _handle = handle;
        BusName = busName;
        Location = location;
        _sharedMutex = SmbusMutexScope.TryOpenSharedMutex();
    }

    public string BusName { get; }

    public string Location { get; }

    public static PawnIoSmbusController? TryOpenPiix4(int port, ILogger logger)
    {
        if (port is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "Only safe PIIX4 ports 0 and 1 are permitted.");
        }

        var handle = TryLoadModule("LibreHardwareMonitor.Resources.PawnIo.SmbusPIIX4.bin", logger);
        if (handle is null)
        {
            return null;
        }

        var keep = false;
        try
        {
            using var tempMutex = SmbusMutexScope.TryOpenSharedMutex();
            using var mutexScope = SmbusMutexScope.Acquire(tempMutex);
            Span<ulong> portIn = [(ulong)port];
            Span<ulong> portOut = stackalloc ulong[1];
            if (!TryExecute(handle, "ioctl_piix4_port_sel", portIn, portOut))
            {
                return null;
            }

            var location = DescribeIdentity(handle, $"PIIX4 port {port}");
            keep = true;
            return new PawnIoSmbusController(handle, $"piix4_{port}", location);
        }
        finally
        {
            if (!keep)
            {
                handle.Dispose();
            }
        }
    }

    public static PawnIoSmbusController? TryOpenI801(ILogger logger)
    {
        var handle = TryLoadModule("LibreHardwareMonitor.Resources.PawnIo.SmbusI801.bin", logger);
        if (handle is null)
        {
            return null;
        }

        var location = DescribeIdentity(handle, "i801");
        return new PawnIoSmbusController(handle, "i801", location);
    }

    public IDisposable AcquireBusLock()
    {
        if (_busLockDepth > 0)
        {
            _busLockDepth++;
            return new BusLockScope(default, isOuter: false);
        }

        ProcessBusLock.Enter();
        SmbusMutexScope mutexScope = default;
        try
        {
            mutexScope = SmbusMutexScope.Acquire(_sharedMutex);
            _busLockDepth = 1;
            return new BusLockScope(mutexScope, isOuter: true);
        }
        catch
        {
            mutexScope.Dispose();
            ProcessBusLock.Exit();
            throw;
        }
    }

    public bool TryWriteByteData(byte address, byte register, byte value)
    {
        SmbusAddressFirewall.ValidateAddress(address);

        if (_busLockDepth > 0)
        {
            return TryWriteByteDataCore(address, register, value);
        }

        lock (ProcessBusLock)
        {
            using var mutexScope = SmbusMutexScope.Acquire(_sharedMutex);
            return TryWriteByteDataCore(address, register, value);
        }
    }

    private bool TryWriteByteDataCore(byte address, byte register, byte value)
    {
        if (_disposed)
        {
            return false;
        }

        Span<ulong> input =
        [
            address,
            SmbusWrite,
            register,
            SmbusByteData,
            value,
            0,
            0,
            0,
            0,
        ];
        Span<ulong> output = stackalloc ulong[5];
        return TryExecute(_handle, "ioctl_smbus_xfer", input, output);
    }

    public bool TryReadWordData(byte address, byte register, out ushort value)
    {
        SmbusAddressFirewall.ValidateAddress(address);

        if (_busLockDepth > 0)
        {
            return TryReadWordDataCore(address, register, out value);
        }

        lock (ProcessBusLock)
        {
            using var mutexScope = SmbusMutexScope.Acquire(_sharedMutex);
            return TryReadWordDataCore(address, register, out value);
        }
    }

    private bool TryReadWordDataCore(byte address, byte register, out ushort value)
    {
        value = 0;
        if (_disposed)
        {
            return false;
        }

        Span<ulong> input =
        [
            address,
            SmbusRead,
            register,
            SmbusWordData,
            0,
            0,
            0,
            0,
            0,
        ];
        Span<ulong> output = stackalloc ulong[5];
        if (!TryExecute(_handle, "ioctl_smbus_xfer", input, output))
        {
            return false;
        }

        value = (ushort)(output[0] & 0xFFFF);
        return true;
    }

    private sealed class BusLockScope(SmbusMutexScope mutexScope, bool isOuter) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _busLockDepth--;
            if (isOuter)
            {
                try
                {
                    mutexScope.Dispose();
                }
                finally
                {
                    ProcessBusLock.Exit();
                }
            }
        }
    }

    public void Dispose()
    {
        lock (ProcessBusLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _sharedMutex?.Dispose();
            _handle.Dispose();
        }
    }

    private static string DescribeIdentity(SafeFileHandle handle, string fallback)
    {
        Span<ulong> input = [0];
        Span<ulong> output = stackalloc ulong[3];
        if (!TryExecute(handle, "ioctl_identity", input, output))
        {
            return $"PawnIO {fallback}";
        }

        Span<byte> nameBytes = stackalloc byte[8];
        BinaryPrimitivesWriteUInt64LittleEndian(nameBytes, output[0]);
        var len = nameBytes.IndexOf((byte)0);
        if (len < 0)
        {
            len = nameBytes.Length;
        }

        var chip = len > 0 ? Encoding.ASCII.GetString(nameBytes[..len]) : fallback;
        var ioBase = output[1];
        var pciVendor = (ushort)(output[2] & 0xFFFF);
        var pciDevice = (ushort)((output[2] >> 16) & 0xFFFF);
        return $"PawnIO {chip} ({fallback}, base 0x{ioBase:X4}, PCI {pciVendor:X4}:{pciDevice:X4})";
    }

    private static void BinaryPrimitivesWriteUInt64LittleEndian(Span<byte> destination, ulong value)
    {
        for (var i = 0; i < 8; i++)
        {
            destination[i] = (byte)(value >> (8 * i));
        }
    }

    private static unsafe SafeFileHandle? TryLoadModule(string resourceName, ILogger logger)
    {
        using var stream = typeof(PawnIo).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        var moduleBytes = new byte[stream.Length];
        stream.ReadExactly(moduleBytes);

        var handle = CreateFileW(
            PawnIoDevicePath,
            FileAccessGenericReadWrite,
            FileShareReadWrite,
            nint.Zero,
            OpenExisting,
            FileAttributeNormal,
            nint.Zero);

        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        fixed (byte* pModule = moduleBytes)
        {
            if (DeviceIoControl(
                handle,
                IoctlLoadBinary,
                pModule,
                (uint)moduleBytes.Length,
                null,
                0,
                out _,
                nint.Zero))
            {
                return handle;
            }
        }

        var error = Marshal.GetLastWin32Error();
        logger.LogDebug("PawnIO module {Module} did not load on this chipset (Win32 error {Error})", resourceName, error);
        handle.Dispose();
        return null;
    }

    private static unsafe bool TryExecute(SafeFileHandle handle, string functionName, ReadOnlySpan<ulong> input, Span<ulong> output)
    {
        var inByteCount = FunctionNameBytes + (input.Length * sizeof(ulong));
        Span<byte> inBuffer = stackalloc byte[inByteCount];
        inBuffer.Clear();

        var nameLen = Math.Min(FunctionNameBytes - 1, functionName.Length);
        Encoding.ASCII.GetBytes(functionName.AsSpan(0, nameLen), inBuffer[..nameLen]);
        MemoryMarshal.AsBytes(input).CopyTo(inBuffer[FunctionNameBytes..]);

        var outBytes = MemoryMarshal.AsBytes(output);
        outBytes.Clear();

        fixed (byte* pIn = inBuffer)
        fixed (byte* pOut = outBytes)
        {
            return DeviceIoControl(
                handle,
                IoctlExecuteFn,
                pIn,
                (uint)inBuffer.Length,
                pOut,
                (uint)outBytes.Length,
                out _,
                nint.Zero);
        }
    }

    private const uint FileAccessGenericReadWrite = 0x80000000u | 0x40000000u;
    private const uint FileShareReadWrite = 0x00000001u | 0x00000002u;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080u;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        void* lpInBuffer,
        uint nInBufferSize,
        void* lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        nint lpOverlapped);
}

/// <summary>
/// Acquires the system-wide <c>Access_SMBUS.HTP.Method</c> mutex used across hardware monitoring tools on Windows.
/// </summary>
[SupportedOSPlatform("windows")]
internal readonly struct SmbusMutexScope : IDisposable
{
    private readonly Mutex? _mutex;
    private readonly bool _acquired;

    private SmbusMutexScope(Mutex? mutex, bool acquired)
    {
        _mutex = mutex;
        _acquired = acquired;
    }

    public static Mutex? TryOpenSharedMutex() =>
        TryCreateMutex(@"Global\Access_SMBUS.HTP.Method")
        ?? TryCreateMutex(@"Local\Access_SMBUS.HTP.Method");

    public static SmbusMutexScope Acquire(Mutex? mutex)
    {
        if (mutex is null)
        {
            return default;
        }

        try
        {
            var acquired = mutex.WaitOne(TimeSpan.FromMilliseconds(1000));
            return new SmbusMutexScope(mutex, acquired);
        }
        catch (AbandonedMutexException)
        {
            // Another process exited while holding the SMBus mutex; we now own it.
            return new SmbusMutexScope(mutex, acquired: true);
        }
        catch
        {
            return default;
        }
    }

    public void Dispose()
    {
        if (_mutex is not null && _acquired)
        {
            _mutex.ReleaseMutex();
        }
    }

    private static Mutex? TryCreateMutex(string name)
    {
        try
        {
            return new Mutex(initiallyOwned: false, name);
        }
        catch
        {
            return null;
        }
    }
}
