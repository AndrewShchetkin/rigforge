using RigForge.Core.Interfaces;

namespace RigForge.Infrastructure.CrossPlatform.Platform;

/// <summary>Default timer; Linux hrtimers already give ~1 ms sleep precision.</summary>
internal sealed class SleepPrecisionTimer : IPrecisionTimer
{
    public void Sleep(TimeSpan duration)
    {
        if (duration > TimeSpan.Zero)
        {
            Thread.Sleep(duration);
        }
    }
}
