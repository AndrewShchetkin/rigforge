using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using RigForge.Core.Interfaces;

namespace RigForge.Infrastructure.Linux.Platform;

/// <summary>
/// Frame pacing via <c>clock_nanosleep(CLOCK_MONOTONIC)</c>. Relative sleeps interrupted by a signal
/// resume from the remaining time. A non-positive duration falls back to <see cref="Thread.Sleep(TimeSpan)"/>.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class NanosleepPrecisionTimer : IPrecisionTimer
{
    private const int ClockMonotonic = 1;
    private const int Eintr = 4;

    public void Sleep(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || !OperatingSystem.IsLinux())
        {
            Thread.Sleep(duration < TimeSpan.Zero ? TimeSpan.Zero : duration);
            return;
        }

        var request = ToTimespec(duration);
        while (true)
        {
            var remain = default(Timespec);
            var result = ClockNanosleep(ClockMonotonic, 0, ref request, ref remain);
            if (result == 0)
            {
                return;
            }

            if (result == Eintr)
            {
                request = remain;
                if (request.TvSec == 0 && request.TvNsec <= 0)
                {
                    return;
                }

                continue;
            }

            Thread.Sleep(duration);
            return;
        }
    }

    private static Timespec ToTimespec(TimeSpan duration)
    {
        var nanoseconds = duration.Ticks * 100L;
        return new Timespec
        {
            TvSec = nanoseconds / 1_000_000_000L,
            TvNsec = nanoseconds % 1_000_000_000L,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec
    {
        public long TvSec;
        public long TvNsec;
    }

    [LibraryImport("libc", EntryPoint = "clock_nanosleep")]
    private static partial int ClockNanosleep(int clockId, int flags, ref Timespec request, ref Timespec remain);
}
