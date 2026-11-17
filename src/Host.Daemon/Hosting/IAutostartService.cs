namespace RigForge.Host.Daemon.Hosting;

/// <summary>Whether sign-in autostart is available and currently turned on.</summary>
public readonly record struct AutostartStatus(bool Supported, bool Enabled, bool TaskRegistered, string? Details = null);

/// <summary>
/// Reads and changes "start when I sign in" without stopping a daemon that is already running.
/// </summary>
public interface IAutostartService
{
    /// <summary>Raised after a successful change so open UI windows can refresh.</summary>
    event Action? Changed;

    AutostartStatus GetStatus();

    Task<AutostartStatus> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}

/// <summary>Used where the operating system has no sign-in autostart integration.</summary>
public sealed class NullAutostartService : IAutostartService
{
    public event Action? Changed
    {
        add { }
        remove { }
    }

    public AutostartStatus GetStatus() => new(Supported: false, Enabled: false, TaskRegistered: false, Details: "Autostart is not supported on this operating system.");

    public Task<AutostartStatus> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        Task.FromException<AutostartStatus>(new InvalidOperationException("Autostart is not supported on this operating system."));
}
