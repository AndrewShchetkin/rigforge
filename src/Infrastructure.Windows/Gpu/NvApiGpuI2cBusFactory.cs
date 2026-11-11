using System.Globalization;
using System.Runtime.Versioning;
using RigForge.Infrastructure.CrossPlatform.Gpu;

namespace RigForge.Infrastructure.Windows.Gpu;

/// <summary>
/// Opens the internal RGB I2C port (port 1) of every NVIDIA GPU through the driver's user-mode <c>nvapi64.dll</c>.
/// No administrator rights, no kernel driver. Every transfer is gated by <see cref="GpuI2cAddressFirewall"/>, so DDC/EDID
/// (0x50..0x57) and VRM addresses can never be reached.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class NvApiGpuI2cBusFactory : IGpuI2cBusFactory
{
    public string BackendName => "NVAPI";

    public bool IsAvailable => NvApiLibrary.TryGet(out _) is not null;

    public string DiagnosticStatus
    {
        get
        {
            _ = NvApiLibrary.TryGet(out var status);
            return status;
        }
    }

    public IReadOnlyList<IGpuI2cBus> OpenBuses()
    {
        if (NvApiLibrary.TryGet(out _) is not { } library)
        {
            return [];
        }

        var handles = library.EnumeratePhysicalGpus();
        var buses = new List<IGpuI2cBus>(handles.Count);
        for (var index = 0; index < handles.Count; index++)
        {
            var handle = handles[index];
            if (!library.TryGetPciIdentity(handle, out var pci))
            {
                continue;
            }

            var adapterName = library.GetFullName(handle);
            string busId;
            string location;
            if (library.TryGetBusId(handle, out var pciBus))
            {
                var hasSlot = library.TryGetBusSlotId(handle, out var slot);
                busId = hasSlot
                    ? string.Create(CultureInfo.InvariantCulture, $"nvapi-{pciBus:x2}-{slot:x2}")
                    : string.Create(CultureInfo.InvariantCulture, $"nvapi-{pciBus:x2}");
                location = string.Create(CultureInfo.InvariantCulture, $"NVAPI GPU {index} (PCI bus {pciBus}, port 1)");
            }
            else
            {
                busId = string.Create(CultureInfo.InvariantCulture, $"nvapi-{index}");
                location = string.Create(CultureInfo.InvariantCulture, $"NVAPI GPU {index} (port 1)");
            }

            buses.Add(new NvApiGpuI2cBus(library, handle, busId, adapterName, location, pci));
        }

        return buses;
    }
}

/// <summary>
/// One GPU's internal RGB port. <see cref="AcquireBusLock"/> returns a single pre-allocated scope (re-entrant per thread
/// via <see cref="Monitor"/>, cross-process via the <c>Access_PCI</c> mutex shared with other RGB tools), so holding the
/// lock for every frame allocates nothing.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class NvApiGpuI2cBus : IGpuI2cBus
{
    private const int MutexTimeoutMilliseconds = 1000;
    private const int MaxTransferBytes = 256;

    private static readonly Lazy<Mutex?> SharedMutex = new(CreateSharedMutex);

    private readonly NvApiLibrary? _library;
    private readonly nint _gpu;
    private readonly object _sync = new();
    private readonly LockScope _scope;
    private int _depth;
    private bool _mutexHeld;
    private volatile bool _disposed;

    internal NvApiGpuI2cBus(NvApiLibrary? library, nint gpu, string busId, string adapterName, string location, GpuPciIdentity pciId)
    {
        _library = library;
        _gpu = gpu;
        BusId = busId;
        AdapterName = adapterName;
        Location = location;
        PciId = pciId;
        _scope = new LockScope(this);
    }

    public string BusId { get; }

    public string AdapterName { get; }

    public string Location { get; }

    public GpuPciIdentity PciId { get; }

    /// <summary>Bus without a loaded NVAPI: every transfer fails after the firewall check. For unit tests.</summary>
    internal static NvApiGpuI2cBus CreateForTests() =>
        new(null, nint.Zero, "nvapi-test", "NVIDIA test GPU", "NVAPI test", default);

    public IDisposable AcquireBusLock()
    {
        EnterLock();
        return _scope;
    }

    public bool TryWriteRegisterBlock(byte slaveAddress, ReadOnlySpan<byte> registerAddress, ReadOnlySpan<byte> data)
    {
        GpuI2cAddressFirewall.ValidateSlaveAddress(slaveAddress);
        if (registerAddress.Length is 0 or > MaxTransferBytes || data.Length is 0 or > MaxTransferBytes)
        {
            return false;
        }

        fixed (byte* pRegister = registerAddress)
        fixed (byte* pData = data)
        {
            return Transfer(write: true, slaveAddress, pRegister, (uint)registerAddress.Length, pData, (uint)data.Length);
        }
    }

    public bool TryReadRegisterBlock(byte slaveAddress, ReadOnlySpan<byte> registerAddress, Span<byte> destination)
    {
        GpuI2cAddressFirewall.ValidateSlaveAddress(slaveAddress);
        if (registerAddress.Length is 0 or > MaxTransferBytes || destination.Length is 0 or > MaxTransferBytes)
        {
            return false;
        }

        fixed (byte* pRegister = registerAddress)
        fixed (byte* pData = destination)
        {
            return Transfer(write: false, slaveAddress, pRegister, (uint)registerAddress.Length, pData, (uint)destination.Length);
        }
    }

    public bool TryWriteRaw(byte slaveAddress, ReadOnlySpan<byte> payload)
    {
        GpuI2cAddressFirewall.ValidateSlaveAddress(slaveAddress);
        if (payload.Length is 0 or > MaxTransferBytes)
        {
            return false;
        }

        fixed (byte* pData = payload)
        {
            return Transfer(write: true, slaveAddress, null, 0, pData, (uint)payload.Length);
        }
    }

    public bool TryReadRaw(byte slaveAddress, Span<byte> destination)
    {
        GpuI2cAddressFirewall.ValidateSlaveAddress(slaveAddress);
        if (destination.Length is 0 or > MaxTransferBytes)
        {
            return false;
        }

        fixed (byte* pData = destination)
        {
            return Transfer(write: false, slaveAddress, null, 0, pData, (uint)destination.Length);
        }
    }

    public void Dispose() => _disposed = true;

    private bool Transfer(bool write, byte slaveAddress, byte* register, uint registerLength, byte* data, uint dataLength)
    {
        if (_disposed || _library is null)
        {
            return false;
        }

        var info = NvI2cInfoV3.ForRgbPort(slaveAddress);
        info.PbI2cRegAddress = register;
        info.RegAddrSize = registerLength;
        info.PbData = data;
        info.Size = dataLength;

        EnterLock();
        try
        {
            return _library.I2cTransfer(_gpu, write, &info) == NvApiLibrary.StatusOk;
        }
        finally
        {
            ExitLock();
        }
    }

    private void EnterLock()
    {
        Monitor.Enter(_sync);
        try
        {
            if (_depth == 0)
            {
                _mutexHeld = TryAcquireSharedMutex();
            }

            _depth++;
        }
        catch
        {
            Monitor.Exit(_sync);
            throw;
        }
    }

    private void ExitLock()
    {
        try
        {
            if (_depth > 0)
            {
                _depth--;
            }

            if (_depth == 0 && _mutexHeld)
            {
                _mutexHeld = false;
                SharedMutex.Value?.ReleaseMutex();
            }
        }
        finally
        {
            Monitor.Exit(_sync);
        }
    }

    private static bool TryAcquireSharedMutex()
    {
        var mutex = SharedMutex.Value;
        if (mutex is null)
        {
            return false;
        }

        try
        {
            return mutex.WaitOne(MutexTimeoutMilliseconds);
        }
        catch (AbandonedMutexException)
        {
            // Another RGB tool exited while holding Access_PCI; this thread owns it now.
            return true;
        }
    }

    /// <summary>Opens <c>Global\Access_PCI</c>, falling back to <c>Local\Access_PCI</c> when the global namespace is denied.</summary>
    private static Mutex? CreateSharedMutex()
    {
        try
        {
            return new Mutex(initiallyOwned: false, @"Global\Access_PCI");
        }
        catch (UnauthorizedAccessException)
        {
            return TryCreateLocalMutex();
        }
        catch (IOException)
        {
            return TryCreateLocalMutex();
        }
    }

    private static Mutex? TryCreateLocalMutex()
    {
        try
        {
            return new Mutex(initiallyOwned: false, @"Local\Access_PCI");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            return null;
        }
    }

    private sealed class LockScope(NvApiGpuI2cBus owner) : IDisposable
    {
        public void Dispose() => owner.ExitLock();
    }
}
