using RigForge.Core.Models;

namespace RigForge.Core.Interfaces;

/// <summary>Controls PWM/DC fan and pump outputs.</summary>
public interface IFanController : IHardwareDevice
{
    IReadOnlyList<FanChannel> Channels { get; }

    /// <summary>Sets a fixed duty; implementations clamp to the channel's supported range.</summary>
    ValueTask SetDutyAsync(string channelId, int dutyPercent, CancellationToken cancellationToken);

    /// <summary>
    /// Hands the channel back to autonomous control (firmware curve, BIOS Smart Fan...) so it stays safe
    /// while the daemon is not running. Called on shutdown and when a curve is removed.
    /// </summary>
    ValueTask RestoreDefaultAsync(string channelId, CancellationToken cancellationToken);
}
