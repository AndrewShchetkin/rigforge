using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Infrastructure.Windows.Hardware;

/// <summary>CPU load and memory usage from plain Win32 APIs (no driver, no admin rights).</summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsSystemDevice : ISensorProvider
{
    private const string DeviceId = "windows:system";

    private ulong _previousIdle;
    private ulong _previousTotal;

    public string Id => DeviceId;

    public string Name => "Windows system";

    public string Vendor => "Microsoft";

    public DeviceCapabilities Capabilities => DeviceCapabilities.Sensors;

    public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>
    {
        ["OS"] = Environment.OSVersion.VersionString,
        ["LogicalProcessors"] = Environment.ProcessorCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    public ValueTask<IReadOnlyList<SensorReading>> ReadSensorsAsync(CancellationToken cancellationToken)
    {
        var readings = new List<SensorReading>(3);

        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            // Kernel time includes idle time.
            var total = kernel + user;
            var totalDelta = total - _previousTotal;
            var idleDelta = idle - _previousIdle;
            if (_previousTotal != 0 && totalDelta > 0)
            {
                var load = 100f * (1f - idleDelta / (float)totalDelta);
                readings.Add(new SensorReading($"{DeviceId}/cpu-load", "CPU load", SensorKind.Load, Math.Clamp(load, 0f, 100f)) { Role = SensorRole.CpuLoad });
            }

            _previousIdle = idle;
            _previousTotal = total;
        }

        var memory = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref memory))
        {
            readings.Add(new SensorReading($"{DeviceId}/memory-load", "Memory load", SensorKind.Load, memory.MemoryLoad));
            readings.Add(new SensorReading($"{DeviceId}/memory-used", "Memory used", SensorKind.Data, (memory.TotalPhys - memory.AvailPhys) / (1024f * 1024f)));
        }

        return ValueTask.FromResult<IReadOnlyList<SensorReading>>(readings);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}
