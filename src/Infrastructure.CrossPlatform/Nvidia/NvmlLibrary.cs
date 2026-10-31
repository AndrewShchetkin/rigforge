using System.Runtime.InteropServices;
using System.Text;

namespace RigForge.Infrastructure.CrossPlatform.Nvidia;

/// <summary>
/// Minimal NVML binding resolved at runtime (nvml.dll ships with the Windows driver, libnvidia-ml.so.1 with the
/// Linux driver). Function pointers avoid a per-assembly DllImport resolver and stay AOT friendly.
/// </summary>
internal sealed unsafe class NvmlLibrary : IDisposable
{
    private const int Success = 0;
    private const int TemperatureGpu = 0;
    private const int ClockGraphics = 0;
    private const int ClockMemory = 2;

    private readonly nint _library;
    private readonly delegate* unmanaged<int> _shutdown;
    private readonly delegate* unmanaged<uint*, int> _getCount;
    private readonly delegate* unmanaged<uint, nint*, int> _getHandleByIndex;
    private readonly delegate* unmanaged<nint, byte*, uint, int> _getName;
    private readonly delegate* unmanaged<nint, byte*, uint, int> _getUuid;
    private readonly delegate* unmanaged<nint, int, uint*, int> _getTemperature;
    private readonly delegate* unmanaged<nint, Utilization*, int> _getUtilization;
    private readonly delegate* unmanaged<nint, uint*, int> _getPowerUsage;
    private readonly delegate* unmanaged<nint, uint*, int> _getFanSpeed;
    private readonly delegate* unmanaged<nint, int, uint*, int> _getClock;
    private readonly delegate* unmanaged<nint, Memory*, int> _getMemory;
    private int _references = 1;

    private NvmlLibrary(nint library)
    {
        _library = library;
        var init = (delegate* unmanaged<int>)NativeLibrary.GetExport(library, "nvmlInit_v2");
        _shutdown = (delegate* unmanaged<int>)NativeLibrary.GetExport(library, "nvmlShutdown");
        _getCount = (delegate* unmanaged<uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetCount_v2");
        _getHandleByIndex = (delegate* unmanaged<uint, nint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetHandleByIndex_v2");
        _getName = (delegate* unmanaged<nint, byte*, uint, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetName");
        _getUuid = (delegate* unmanaged<nint, byte*, uint, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetUUID");
        _getTemperature = (delegate* unmanaged<nint, int, uint*, int>)Optional(library, "nvmlDeviceGetTemperature");
        _getUtilization = (delegate* unmanaged<nint, Utilization*, int>)Optional(library, "nvmlDeviceGetUtilizationRates");
        _getPowerUsage = (delegate* unmanaged<nint, uint*, int>)Optional(library, "nvmlDeviceGetPowerUsage");
        _getFanSpeed = (delegate* unmanaged<nint, uint*, int>)Optional(library, "nvmlDeviceGetFanSpeed");
        _getClock = (delegate* unmanaged<nint, int, uint*, int>)Optional(library, "nvmlDeviceGetClockInfo");
        _getMemory = (delegate* unmanaged<nint, Memory*, int>)Optional(library, "nvmlDeviceGetMemoryInfo");

        var result = init();
        if (result != Success)
        {
            throw new InvalidOperationException($"nvmlInit_v2 failed with code {result}.");
        }
    }

    public static NvmlLibrary? TryLoad(out string? error)
    {
        foreach (var candidate in Candidates())
        {
            if (!NativeLibrary.TryLoad(candidate, out var handle))
            {
                continue;
            }

            try
            {
                error = null;
                return new NvmlLibrary(handle);
            }
            catch (Exception ex) when (ex is EntryPointNotFoundException or InvalidOperationException)
            {
                NativeLibrary.Free(handle);
                error = ex.Message;
                return null;
            }
        }

        error = "NVML library not found (no NVIDIA driver installed?)";
        return null;
    }

    public uint DeviceCount
    {
        get
        {
            uint count;
            return _getCount(&count) == Success ? count : 0;
        }
    }

    public nint GetHandle(uint index)
    {
        nint handle;
        return _getHandleByIndex(index, &handle) == Success ? handle : 0;
    }

    public string GetName(nint device) => ReadString(_getName, device);

    public string GetUuid(nint device) => ReadString(_getUuid, device);

    public uint? GetTemperature(nint device) => ReadUInt(_getTemperature, device, TemperatureGpu);

    public (uint Gpu, uint Memory)? GetUtilization(nint device)
    {
        if (_getUtilization == null)
        {
            return null;
        }

        Utilization utilization;
        return _getUtilization(device, &utilization) == Success ? (utilization.Gpu, utilization.Memory) : null;
    }

    public uint? GetPowerMilliwatts(nint device) => ReadUInt(_getPowerUsage, device);

    public uint? GetFanSpeedPercent(nint device) => ReadUInt(_getFanSpeed, device);

    public uint? GetGraphicsClock(nint device) => ReadUInt(_getClock, device, ClockGraphics);

    public uint? GetMemoryClock(nint device) => ReadUInt(_getClock, device, ClockMemory);

    public ulong? GetUsedMemoryBytes(nint device)
    {
        if (_getMemory == null)
        {
            return null;
        }

        Memory memory;
        return _getMemory(device, &memory) == Success ? memory.Used : null;
    }

    /// <summary>Shared by every GPU device of one probe; the library is unloaded with the last reference.</summary>
    public NvmlLibrary AddReference()
    {
        Interlocked.Increment(ref _references);
        return this;
    }

    public void Dispose()
    {
        if (Interlocked.Decrement(ref _references) != 0)
        {
            return;
        }

        _shutdown();
        NativeLibrary.Free(_library);
    }

    private static IEnumerable<string> Candidates()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return "nvml.dll";
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvml.dll");
            var programFiles = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Path.Combine(programFiles, "NVIDIA Corporation", "NVSMI", "nvml.dll");
        }
        else
        {
            yield return "libnvidia-ml.so.1";
            yield return "libnvidia-ml.so";
        }
    }

    private static nint Optional(nint library, string name) =>
        NativeLibrary.TryGetExport(library, name, out var address) ? address : 0;

    private static uint? ReadUInt(delegate* unmanaged<nint, uint*, int> function, nint device)
    {
        if (function == null)
        {
            return null;
        }

        uint value;
        return function(device, &value) == Success ? value : null;
    }

    private static uint? ReadUInt(delegate* unmanaged<nint, int, uint*, int> function, nint device, int kind)
    {
        if (function == null)
        {
            return null;
        }

        uint value;
        return function(device, kind, &value) == Success ? value : null;
    }

    private static string ReadString(delegate* unmanaged<nint, byte*, uint, int> function, nint device)
    {
        const int capacity = 96;
        var buffer = stackalloc byte[capacity];
        if (function(device, buffer, capacity) != Success)
        {
            return string.Empty;
        }

        var span = new ReadOnlySpan<byte>(buffer, capacity);
        var length = span.IndexOf((byte)0);
        return Encoding.UTF8.GetString(length >= 0 ? span[..length] : span);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Utilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Memory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }
}
