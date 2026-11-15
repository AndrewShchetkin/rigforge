namespace RigForge.Host.Daemon.Tray;

/// <summary>
/// Decides when the tray icon is shown and which loopback URL the Electron shell may open.
/// Win32 and process launch stay outside this type so the rules can be tested without a desktop session.
/// </summary>
public static class TrayIconPolicy
{
    public const string FallbackDashboardUrl = "http://127.0.0.1:5757/#/dashboard";
    public const string ShellFileName = "RigForge.exe";

    /// <summary>
    /// The icon belongs to an interactive Windows logon session. A session-0 service has no tray,
    /// and Linux has no Win32 notification area.
    /// </summary>
    public static bool ShouldShow(bool isWindows, bool isWindowsService) =>
        isWindows && !isWindowsService;

    /// <summary>
    /// First http loopback listen address, opened on the dashboard hash route.
    /// Wildcard and non-loopback bindings are skipped. With nothing usable, the local fallback is returned.
    /// </summary>
    public static string DashboardUrl(IEnumerable<string>? listenAddresses)
    {
        if (listenAddresses is not null)
        {
            foreach (var address in listenAddresses)
            {
                if (TryNormalizeDashboardUrl(address) is { } url)
                {
                    return url;
                }
            }
        }

        return FallbackDashboardUrl;
    }

    /// <summary>
    /// Accepts only <c>http://127.0.0.1</c> and <c>http://localhost</c> and rewrites the path to <c>/#/dashboard</c>.
    /// Anything else is refused so the shell cannot be pointed at an arbitrary host.
    /// </summary>
    public static string? TryNormalizeDashboardUrl(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        if (!Uri.TryCreate(candidate.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (uri.UserInfo.Length > 0 || !IsLoopbackHost(uri.IdnHost))
        {
            return null;
        }

        var authority = uri.IsDefaultPort ? uri.IdnHost : $"{uri.IdnHost}:{uri.Port}";
        return $"http://{authority}/#/dashboard";
    }

    /// <summary>First candidate for which <paramref name="exists"/> returns true.</summary>
    public static string? FindShellExecutable(IEnumerable<string> candidates, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(exists);

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Published layout keeps <c>RigForge.exe</c> next to the daemon. <c>dotnet run</c> uses the unpacked
    /// shell under the repository when that directory is known.
    /// </summary>
    public static IEnumerable<string> ShellCandidates(string daemonDirectory, string? repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(daemonDirectory);
        yield return Path.Combine(daemonDirectory, ShellFileName);

        if (!string.IsNullOrWhiteSpace(repositoryRoot))
        {
            yield return Path.Combine(repositoryRoot, "shell", "dist", "win-unpacked", ShellFileName);
        }
    }

    /// <summary>
    /// Walks parents of <paramref name="startDirectory"/> until it finds the repository root
    /// (the folder that contains both <c>Directory.Build.props</c> and <c>src/Host.Daemon</c>).
    /// </summary>
    public static string? FindRepositoryRoot(string startDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startDirectory);

        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))
                && Directory.Exists(Path.Combine(dir.FullName, "src", "Host.Daemon")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static bool IsLoopbackHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);
}
