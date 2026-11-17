using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;

namespace RigForge.Host.Daemon.Hosting;

/// <summary>
/// Commands handled before the web host starts, so the installer can register tasks and stop the daemon
/// without binding port 5757.
/// </summary>
public static class DaemonCliHandler
{
    public const int DefaultShutdownTimeoutMs = 6000;
    public const string DefaultShutdownUrl = "http://127.0.0.1:5757";

    public abstract record DaemonCliCommand;

    public sealed record InstallTaskCommand(string AutostartMode, string? User) : DaemonCliCommand;

    public sealed record UninstallTaskCommand : DaemonCliCommand;

    public sealed record SetAutostartCommand(bool Enabled) : DaemonCliCommand;

    public sealed record ShutdownCommand(int TimeoutMs, string Url) : DaemonCliCommand;

    /// <summary>Re-launches this executable with the Windows runas verb and then exits.</summary>
    public sealed record RunElevatedCommand : DaemonCliCommand;

    public const int ElevationCancelledExitCode = 1223;

    public sealed record ParseResult(DaemonCliCommand? Command, string? Error);

    /// <summary>
    /// Returns <c>null</c> when <paramref name="args"/> is a normal daemon start.
    /// Otherwise the process should exit with the returned code.
    /// </summary>
    public static async Task<int?> TryExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var parsed = Parse(args);
        if (parsed.Error is not null)
        {
            Console.Error.WriteLine(parsed.Error);
            return 1;
        }

        if (parsed.Command is null)
        {
            return null;
        }

        try
        {
            return await ExecuteAsync(parsed.Command, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    public static ParseResult Parse(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0)
        {
            return new ParseResult(null, null);
        }

        string? verb = null;
        string? mode = null;
        string? user = null;
        bool? enabled = null;
        int? timeout = null;
        string? url = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (Is(arg, "--install-task") || Is(arg, "--uninstall-task") || Is(arg, "--shutdown") || Is(arg, "--run-elevated"))
            {
                if (verb is not null)
                {
                    return new ParseResult(null, "Only one of --install-task, --uninstall-task, --set-autostart, --shutdown, or --run-elevated may be used.");
                }

                verb = arg.ToLowerInvariant();
            }
            else if (Is(arg, "--set-autostart"))
            {
                if (verb is not null)
                {
                    return new ParseResult(null, "Only one of --install-task, --uninstall-task, --set-autostart, --shutdown, or --run-elevated may be used.");
                }

                if (!TryTake(args, ref i, out var value) || !bool.TryParse(value, out var parsed))
                {
                    return new ParseResult(null, "--set-autostart requires true or false.");
                }

                verb = "--set-autostart";
                enabled = parsed;
            }
            else if (Is(arg, "--autostart"))
            {
                if (!TryTake(args, ref i, out var value) || !TryNormalizeMode(value, out var normalized))
                {
                    return new ParseResult(null, "--autostart requires true, false, or preserve.");
                }

                mode = normalized;
            }
            else if (Is(arg, "--user"))
            {
                if (!TryTake(args, ref i, out var value) || string.IsNullOrWhiteSpace(value))
                {
                    return new ParseResult(null, "--user requires a Windows account name.");
                }

                user = value;
            }
            else if (Is(arg, "--timeout-ms"))
            {
                if (!TryTake(args, ref i, out var value)
                    || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                    || parsed < 0)
                {
                    return new ParseResult(null, "--timeout-ms requires a non-negative integer.");
                }

                timeout = parsed;
            }
            else if (Is(arg, "--url"))
            {
                if (!TryTake(args, ref i, out var value) || string.IsNullOrWhiteSpace(value))
                {
                    return new ParseResult(null, "--url requires an http loopback address.");
                }

                url = value;
            }
        }

        return verb switch
        {
            "--install-task" => new ParseResult(new InstallTaskCommand(mode ?? "true", user), null),
            "--uninstall-task" => new ParseResult(new UninstallTaskCommand(), null),
            "--set-autostart" => new ParseResult(new SetAutostartCommand(enabled ?? false), null),
            "--shutdown" => new ParseResult(new ShutdownCommand(timeout ?? DefaultShutdownTimeoutMs, url ?? DefaultShutdownUrl), null),
            "--run-elevated" => new ParseResult(new RunElevatedCommand(), null),
            _ => new ParseResult(null, null),
        };
    }

    private static async Task<int> ExecuteAsync(DaemonCliCommand command, CancellationToken cancellationToken)
    {
        switch (command)
        {
            case InstallTaskCommand install:
                if (!OperatingSystem.IsWindows())
                {
                    Console.Error.WriteLine("The RigForge sign-in task is only available on Windows.");
                    return 1;
                }

                return await InstallOnWindowsAsync(install, cancellationToken);
            case UninstallTaskCommand:
                if (!OperatingSystem.IsWindows())
                {
                    Console.Error.WriteLine("The RigForge sign-in task is only available on Windows.");
                    return 1;
                }

                return await UninstallOnWindowsAsync(cancellationToken);
            case SetAutostartCommand set:
                var service = CreateAutostart();
                var status = await service.SetEnabledAsync(set.Enabled, cancellationToken);
                if (!status.Supported)
                {
                    Console.Error.WriteLine(status.Details ?? "Autostart is not supported on this operating system.");
                    return 1;
                }

                return 0;
            case ShutdownCommand shutdown:
                return await ShutdownAsync(shutdown, cancellationToken);
            case RunElevatedCommand:
                if (!OperatingSystem.IsWindows())
                {
                    Console.Error.WriteLine("Elevated launch is only available on Windows.");
                    return 1;
                }

                return RunElevated();
            default:
                return 1;
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<int> InstallOnWindowsAsync(InstallTaskCommand install, CancellationToken cancellationToken)
    {
        var service = new WindowsAutostartService();
        await service.InstallTaskAsync(install.AutostartMode, install.User, executablePath: null, cancellationToken);
        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static async Task<int> UninstallOnWindowsAsync(CancellationToken cancellationToken)
    {
        var service = new WindowsAutostartService();
        await service.UninstallTaskAsync(cancellationToken);
        return 0;
    }

    private static IAutostartService CreateAutostart()
    {
        if (OperatingSystem.IsWindows())
        {
            return CreateWindowsAutostart();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxAutostartService();
        }

        return new NullAutostartService();
    }

    [SupportedOSPlatform("windows")]
    private static WindowsAutostartService CreateWindowsAutostart() => new();

    [SupportedOSPlatform("windows")]
    private static int RunElevated()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            Console.Error.WriteLine("Could not locate RigForge.Daemon.exe.");
            return 1;
        }

        try
        {
            var start = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
                UseShellExecute = true,
                Verb = "runas",
            };
            if (Process.Start(start) is null)
            {
                Console.Error.WriteLine("Windows did not start the elevated daemon.");
                return 1;
            }

            return 0;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ElevationCancelledExitCode)
        {
            return ElevationCancelledExitCode;
        }
    }

    private static async Task<int> ShutdownAsync(ShutdownCommand command, CancellationToken cancellationToken)
    {
        if (!TryCreateShutdownUri(command.Url, out var uri))
        {
            Console.Error.WriteLine("--url must be an http://127.0.0.1 or http://localhost address.");
            return 1;
        }

        try
        {
            using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromSeconds(5),
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, uri);
            using var response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
        }

        await WaitForOtherDaemonsAsync(command.TimeoutMs, cancellationToken);
        return 0;
    }

    private static async Task WaitForOtherDaemonsAsync(int timeoutMs, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var current = Environment.ProcessId;
        while (Environment.TickCount64 < deadline)
        {
            if (!OtherDaemonsExist(current))
            {
                return;
            }

            var remaining = deadline - Environment.TickCount64;
            if (remaining <= 0)
            {
                return;
            }

            await Task.Delay((int)Math.Min(200, remaining), cancellationToken);
        }
    }

    private static bool OtherDaemonsExist(int current)
    {
        var found = false;
        foreach (var process in Process.GetProcessesByName("RigForge.Daemon"))
        {
            try
            {
                if (process.Id != current)
                {
                    found = true;
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        return found;
    }

    private static bool TryCreateShutdownUri(string url, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var baseUri)
            || !baseUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || baseUri.UserInfo.Length > 0)
        {
            return false;
        }

        if (!baseUri.IsLoopback
            && !baseUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            && !baseUri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        uri = new Uri(new Uri(baseUri.GetLeftPart(UriPartial.Authority)), "api/host/shutdown");
        return true;
    }

    private static bool TryNormalizeMode(string value, out string normalized)
    {
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase)
            || value.Equals("preserve", StringComparison.OrdinalIgnoreCase))
        {
            normalized = value.ToLowerInvariant();
            return true;
        }

        normalized = "";
        return false;
    }

    private static bool TryTake(IReadOnlyList<string> args, ref int index, out string value)
    {
        if (index + 1 >= args.Count)
        {
            value = "";
            return false;
        }

        index++;
        value = args[index];
        return true;
    }

    private static bool Is(string argument, string expected) =>
        argument.Equals(expected, StringComparison.OrdinalIgnoreCase);
}
