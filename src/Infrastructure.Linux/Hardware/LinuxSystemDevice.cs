using System.Globalization;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Infrastructure.Linux.Hardware;

/// <summary>CPU load from /proc/stat and memory usage from /proc/meminfo.</summary>
internal sealed class LinuxSystemDevice : ISensorProvider
{
    private const string DeviceId = "linux:system";

    private ulong _previousIdle;
    private ulong _previousTotal;

    public string Id => DeviceId;

    public string Name => "Linux system";

    public string Vendor => "Linux";

    public DeviceCapabilities Capabilities => DeviceCapabilities.Sensors;

    public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>
    {
        ["Kernel"] = Environment.OSVersion.VersionString,
    };

    public async ValueTask<IReadOnlyList<SensorReading>> ReadSensorsAsync(CancellationToken cancellationToken)
    {
        var readings = new List<SensorReading>(3);

        var stat = await File.ReadAllLinesAsync("/proc/stat", cancellationToken);
        if (ParseCpuLine(stat.FirstOrDefault()) is { Total: > 0 } cpu)
        {
            if (_previousTotal != 0 && cpu.Total > _previousTotal)
            {
                var load = 100f * (1f - (cpu.Idle - _previousIdle) / (float)(cpu.Total - _previousTotal));
                readings.Add(new SensorReading($"{DeviceId}/cpu-load", "CPU load", SensorKind.Load, Math.Clamp(load, 0f, 100f)) { Role = SensorRole.CpuLoad });
            }

            (_previousIdle, _previousTotal) = (cpu.Idle, cpu.Total);
        }

        var memory = (await File.ReadAllLinesAsync("/proc/meminfo", cancellationToken))
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => ulong.TryParse(parts[1].Replace("kB", string.Empty, StringComparison.Ordinal).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var kb) ? kb : 0UL);

        if (memory.TryGetValue("MemTotal", out var memTotal) && memory.TryGetValue("MemAvailable", out var available) && memTotal > 0)
        {
            readings.Add(new SensorReading($"{DeviceId}/memory-load", "Memory load", SensorKind.Load, 100f * (memTotal - available) / memTotal));
            readings.Add(new SensorReading($"{DeviceId}/memory-used", "Memory used", SensorKind.Data, (memTotal - available) / 1024f));
        }

        return readings;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>"cpu  user nice system idle iowait irq softirq steal guest guest_nice" → (idle + iowait, sum of the first 8).</summary>
    internal static (ulong Idle, ulong Total)? ParseCpuLine(string? line)
    {
        if (line is null || !line.StartsWith("cpu ", StringComparison.Ordinal))
        {
            return null;
        }

        var values = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Take(8)
            .Select(v => ulong.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0UL)
            .ToArray();

        if (values.Length < 5)
        {
            return null;
        }

        var total = values.Aggregate(0UL, (sum, v) => sum + v);
        return (values[3] + values[4], total);
    }
}
