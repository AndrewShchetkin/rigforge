namespace RigForge.Core.Interfaces;

/// <summary>
/// Thread sleep with ~1 ms precision. The default Windows timer granularity (15.6 ms) would cap
/// a sleep-paced render loop at ~32 FPS, so the Windows implementation uses a high-resolution waitable timer.
/// </summary>
public interface IPrecisionTimer
{
    void Sleep(TimeSpan duration);
}
