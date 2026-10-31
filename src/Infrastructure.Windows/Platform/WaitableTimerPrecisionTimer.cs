using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using RigForge.Core.Interfaces;

namespace RigForge.Infrastructure.Windows.Platform;

/// <summary>
/// Sleeps with ~0.5 ms precision using a high-resolution waitable timer (Windows 10 1803+), without raising the
/// global timer resolution via timeBeginPeriod. Falls back to Thread.Sleep on older systems.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WaitableTimerPrecisionTimer : IPrecisionTimer
{
    private const uint CreateWaitableTimerHighResolution = 0x00000002;
    private const uint TimerAllAccess = 0x1F0003;
    private const uint Infinite = 0xFFFFFFFF;

    [ThreadStatic]
    private static SafeWaitHandle? t_timer;

    public void Sleep(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        var timer = t_timer ??= CreateWaitableTimerExW(0, null, CreateWaitableTimerHighResolution, TimerAllAccess);
        if (timer.IsInvalid)
        {
            Thread.Sleep(duration);
            return;
        }

        // Negative due time = relative interval in 100 ns ticks.
        var dueTime = -duration.Ticks;
        if (!SetWaitableTimer(timer, in dueTime, 0, 0, 0, false))
        {
            Thread.Sleep(duration);
            return;
        }

        _ = WaitForSingleObject(timer, Infinite);
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeWaitHandle CreateWaitableTimerExW(nint timerAttributes, string? timerName, uint flags, uint desiredAccess);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWaitableTimer(SafeWaitHandle timer, in long dueTime, int period, nint completionRoutine, nint argument, [MarshalAs(UnmanagedType.Bool)] bool resume);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
}
