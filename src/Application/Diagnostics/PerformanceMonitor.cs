using System.Diagnostics;
using RigForge.Application.Effects;

namespace RigForge.Application.Diagnostics;

/// <summary>One sample of process and lighting-loop cost. Safe to read from any thread.</summary>
public sealed record PerformanceSnapshot(
    DateTimeOffset Timestamp,
    double CpuPercent,
    long PrivateBytes,
    long WorkingSetBytes,
    long ManagedHeapBytes,
    double EffectFps,
    double FrameTimeMicroseconds,
    EffectRenderMode RenderMode,
    int ActiveRgbDevices,
    int TotalControlledLeds,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections);

/// <summary>
/// Samples process CPU, memory, and the lighting loop. CPU is a delta of <see cref="Environment.CpuUsage"/>
/// over wall-clock time, divided across logical processors, so idle rendering stays near zero.
/// </summary>
public sealed class PerformanceMonitor(EffectRenderEngine effects)
{
    private readonly Lock _gate = new();
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly int _processorCount = Math.Max(1, Environment.ProcessorCount);
    private TimeSpan _previousCpu = Environment.CpuUsage.TotalTime;
    private long _previousTimestamp = Stopwatch.GetTimestamp();

    public PerformanceSnapshot Capture()
    {
        lock (_gate)
        {
            var now = Stopwatch.GetTimestamp();
            var cpu = Environment.CpuUsage.TotalTime;
            var elapsed = Stopwatch.GetElapsedTime(_previousTimestamp, now);
            var cpuDelta = cpu - _previousCpu;
            _previousCpu = cpu;
            _previousTimestamp = now;

            var percent = 0d;
            if (elapsed.TotalSeconds > 0d)
            {
                percent = cpuDelta.TotalSeconds / elapsed.TotalSeconds / _processorCount * 100d;
                if (!double.IsFinite(percent))
                {
                    percent = 0d;
                }

                percent = Math.Clamp(percent, 0d, 100d);
            }

            _process.Refresh();
            return new PerformanceSnapshot(
                DateTimeOffset.UtcNow,
                percent,
                _process.PrivateMemorySize64,
                _process.WorkingSet64,
                GC.GetTotalMemory(forceFullCollection: false),
                effects.FramesPerSecond,
                effects.LastFrameMicroseconds,
                effects.CurrentRenderMode,
                effects.ActiveRgbDeviceCount,
                effects.TotalControlledLeds,
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2));
        }
    }
}
