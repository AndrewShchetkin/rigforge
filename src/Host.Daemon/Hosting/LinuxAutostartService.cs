using System.ComponentModel;

namespace RigForge.Host.Daemon.Hosting;

/// <summary>
/// Toggles the systemd user unit <c>rigforge.service</c>. Enable and disable do not pass <c>--now</c>,
/// so a running daemon is left in place.
/// </summary>
public sealed class LinuxAutostartService : IAutostartService
{
    public const string UnitName = "rigforge.service";
    private const long CacheForMilliseconds = 5000;
    private readonly Lock _gate = new();
    private AutostartStatus? _cached;
    private long _cachedAt;
    private int _generation;

    public event Action? Changed;

    public AutostartStatus GetStatus()
    {
        int generation;
        lock (_gate)
        {
            if (_cached is { } cached && Environment.TickCount64 - _cachedAt < CacheForMilliseconds)
            {
                return cached;
            }

            generation = _generation;
        }

        var status = Query();
        lock (_gate)
        {
            if (generation == _generation)
            {
                _cached = status;
                _cachedAt = Environment.TickCount64;
            }
        }

        return status;
    }

    public async Task<AutostartStatus> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var current = GetStatus();
        if (!current.Supported)
        {
            throw new InvalidOperationException(current.Details ?? "systemd user autostart is not available.");
        }

        var verb = enabled ? "enable" : "disable";
        var result = await ProcessCommand.RunAsync("systemctl", ["--user", verb, UnitName], outputEncoding: null, cancellationToken);
        if (result.ExitCode != 0)
        {
            var details = string.Join(" ", new[] { result.StdErr, result.StdOut }.Where(static part => !string.IsNullOrWhiteSpace(part))).Trim();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(details)
                ? $"systemctl --user {verb} {UnitName} exited {result.ExitCode}."
                : $"systemctl --user {verb} {UnitName} exited {result.ExitCode}: {details}");
        }

        Invalidate();
        var status = GetStatus();
        Changed?.Invoke();
        return status;
    }

    private AutostartStatus Query()
    {
        var unitInstalled = UnitCandidates().Any(File.Exists);
        var systemctl = IsSystemctlAvailable();
        if (!unitInstalled || !systemctl)
        {
            var details = !systemctl
                ? "systemctl is not available."
                : "rigforge.service is not installed for this user.";
            return new AutostartStatus(Supported: false, Enabled: false, TaskRegistered: unitInstalled, Details: details);
        }

        try
        {
            var result = ProcessCommand.Run("systemctl", ["--user", "is-enabled", UnitName], outputEncoding: null, CancellationToken.None);
            var text = result.StdOut.Trim();
            var enabled = result.ExitCode == 0 && text.Equals("enabled", StringComparison.OrdinalIgnoreCase);
            return new AutostartStatus(Supported: true, enabled, TaskRegistered: true, Details: string.IsNullOrWhiteSpace(text) ? null : text);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or IOException)
        {
            return new AutostartStatus(Supported: true, Enabled: false, TaskRegistered: true, Details: exception.Message);
        }
    }

    private void Invalidate()
    {
        lock (_gate)
        {
            _generation++;
            _cached = null;
        }
    }

    private static IEnumerable<string> UnitCandidates()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            yield return Path.Combine(home, ".config", "systemd", "user", UnitName);
        }

        yield return "/usr/lib/systemd/user/" + UnitName;
        yield return "/etc/systemd/user/" + UnitName;
    }

    private static bool IsSystemctlAvailable()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (File.Exists(Path.Combine(directory, "systemctl")))
                {
                    return true;
                }
            }
        }

        return File.Exists("/usr/bin/systemctl") || File.Exists("/bin/systemctl");
    }
}
