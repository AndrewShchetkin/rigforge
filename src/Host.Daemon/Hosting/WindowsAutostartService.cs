using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;

namespace RigForge.Host.Daemon.Hosting;

/// <summary>
/// Registers the per-user <c>\RigForge</c> logon task. The interactive account is taken from the
/// explorer process in the active session so an over-the-shoulder UAC install does not bind the task
/// to the administrator who approved the prompt.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAutostartService : IAutostartService
{
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

        if (!TryQuery(out var status, out var cacheable))
        {
            return status;
        }

        lock (_gate)
        {
            if (cacheable && generation == _generation)
            {
                _cached = status;
                _cachedAt = Environment.TickCount64;
            }
        }

        return status;
    }

    public Task<AutostartStatus> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        InstallTaskAsync(enabled ? "true" : "false", explicitUser: null, executablePath: null, cancellationToken);

    public async Task<AutostartStatus> InstallTaskAsync(string autostartMode, string? explicitUser, string? executablePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(autostartMode);
        var existing = await TryReadTaskAsync(cancellationToken);
        var logonTrigger = WindowsLogonTaskXml.ResolveLogonTrigger(
            autostartMode,
            taskExists: existing is not null,
            currentLogonTriggerEnabled: existing?.LogonTriggerEnabled ?? false);
        var (executable, workingDirectory) = ResolvePaths(executablePath, existing);
        var user = ResolveUser(explicitUser, existing);
        var xml = WindowsLogonTaskXml.BuildXml(executable, workingDirectory, user, logonTrigger);
        var tempPath = Path.Combine(Path.GetTempPath(), "rigforge-task-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            await File.WriteAllTextAsync(tempPath, xml, new UnicodeEncoding(bigEndian: false, byteOrderMark: true), cancellationToken);
            var created = await RunAsync(xmlOutput: false, cancellationToken, "/Create", "/TN", WindowsLogonTaskXml.TaskName, "/XML", tempPath, "/F");
            if (created.ExitCode != 0)
            {
                throw new InvalidOperationException(Failure("Could not register the RigForge sign-in task", created));
            }
        }
        finally
        {
            TryDelete(tempPath);
        }

        return PublishFreshStatus();
    }

    public async Task UninstallTaskAsync(CancellationToken cancellationToken = default)
    {
        _ = await RunAsync(xmlOutput: false, cancellationToken, "/End", "/TN", WindowsLogonTaskXml.TaskName);
        var query = await RunAsync(xmlOutput: true, cancellationToken, "/Query", "/TN", WindowsLogonTaskXml.TaskName, "/XML");
        if (query.ExitCode == 0)
        {
            var deleted = await RunAsync(xmlOutput: false, cancellationToken, "/Delete", "/TN", WindowsLogonTaskXml.TaskName, "/F");
            if (deleted.ExitCode != 0 && !IsMissing(deleted))
            {
                throw new InvalidOperationException(Failure("Could not remove the RigForge sign-in task", deleted));
            }
        }
        else if (IsAccessDenied(query))
        {
            throw new InvalidOperationException(Failure("Could not remove the RigForge sign-in task", query));
        }

        Publish(new AutostartStatus(Supported: true, Enabled: false, TaskRegistered: false));
    }

    private AutostartStatus PublishFreshStatus()
    {
        Invalidate();
        var status = GetStatus();
        Changed?.Invoke();
        return status;
    }

    private void Publish(AutostartStatus status)
    {
        lock (_gate)
        {
            _generation++;
            _cached = status;
            _cachedAt = Environment.TickCount64;
        }

        Changed?.Invoke();
    }

    private void Invalidate()
    {
        lock (_gate)
        {
            _generation++;
            _cached = null;
        }
    }

    private async Task<ParsedTaskInfo?> TryReadTaskAsync(CancellationToken cancellationToken)
    {
        var query = await RunAsync(xmlOutput: true, cancellationToken, "/Query", "/TN", WindowsLogonTaskXml.TaskName, "/XML");
        if (query.ExitCode == 0 && WindowsLogonTaskXml.TryParse(query.StdOut, out var info))
        {
            return info;
        }

        if (IsAccessDenied(query))
        {
            throw new InvalidOperationException(Failure("Could not read the RigForge sign-in task", query));
        }

        return null;
    }

    private bool TryQuery(out AutostartStatus status, out bool cacheable)
    {
        try
        {
            var query = Run(xmlOutput: true, "/Query", "/TN", WindowsLogonTaskXml.TaskName, "/XML");
            if (query.ExitCode == 0 && WindowsLogonTaskXml.TryParse(query.StdOut, out var info))
            {
                status = new AutostartStatus(Supported: true, info.AutostartEnabled, TaskRegistered: true);
                cacheable = true;
                return true;
            }

            if (IsAccessDenied(query))
            {
                status = new AutostartStatus(Supported: true, Enabled: false, TaskRegistered: false, Details: Combine(query));
                cacheable = false;
                return true;
            }

            status = new AutostartStatus(Supported: true, Enabled: false, TaskRegistered: false);
            cacheable = true;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or IOException)
        {
            status = new AutostartStatus(Supported: true, Enabled: false, TaskRegistered: false, Details: exception.Message);
            cacheable = false;
            return true;
        }
    }

    private static (string Executable, string WorkingDirectory) ResolvePaths(string? explicitExecutable, ParsedTaskInfo? existing)
    {
        if (!string.IsNullOrWhiteSpace(explicitExecutable))
        {
            return (explicitExecutable, DirectoryOf(explicitExecutable));
        }

        if (existing?.ExecutablePath is { } current && File.Exists(current))
        {
            var workingDirectory = existing.Value.WorkingDirectory;
            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                workingDirectory = DirectoryOf(current);
            }

            return (current, workingDirectory);
        }

        var executable = Path.Combine(AppContext.BaseDirectory, "RigForge.Daemon.exe");
        return (executable, AppContext.BaseDirectory);
    }

    private static string DirectoryOf(string executable)
    {
        var directory = Path.GetDirectoryName(executable);
        return string.IsNullOrWhiteSpace(directory) ? AppContext.BaseDirectory : directory;
    }

    private static string ResolveUser(string? explicitUser, ParsedTaskInfo? existing)
    {
        if (!string.IsNullOrWhiteSpace(explicitUser))
        {
            return explicitUser;
        }

        if (existing?.UserId is { Length: > 0 } userId)
        {
            return userId;
        }

        return InteractiveSessionUser.Resolve();
    }

    private static ProcessCommand.Result Run(bool xmlOutput, params string[] arguments) =>
        ProcessCommand.Run("schtasks.exe", arguments, xmlOutput ? Encoding.Unicode : Console.OutputEncoding, CancellationToken.None);

    private static Task<ProcessCommand.Result> RunAsync(bool xmlOutput, CancellationToken cancellationToken, params string[] arguments) =>
        ProcessCommand.RunAsync("schtasks.exe", arguments, xmlOutput ? Encoding.Unicode : Console.OutputEncoding, cancellationToken);

    private static string Failure(string action, ProcessCommand.Result result)
    {
        var details = Combine(result);
        return string.IsNullOrWhiteSpace(details)
            ? $"{action}. schtasks.exe exited {result.ExitCode}."
            : $"{action}. schtasks.exe exited {result.ExitCode}: {details}";
    }

    private static string Combine(ProcessCommand.Result result)
    {
        var text = string.Join(" ", new[] { result.StdErr, result.StdOut }.Where(static part => !string.IsNullOrWhiteSpace(part))).Trim();
        return text.Length > 600 ? text[..600] : text;
    }

    private static bool IsAccessDenied(ProcessCommand.Result result)
    {
        var text = Combine(result);
        return text.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Отказано в доступе", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMissing(ProcessCommand.Result result)
    {
        var text = Combine(result);
        return text.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
            || text.Contains("cannot find the file", StringComparison.OrdinalIgnoreCase)
            || text.Contains("не существует", StringComparison.OrdinalIgnoreCase)
            || text.Contains("не удается найти", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static class InteractiveSessionUser
    {
        public static string Resolve()
        {
            try
            {
                var currentSession = Process.GetCurrentProcess().SessionId;
                var consoleSession = ConsoleSessionId();
                string? currentUser = null;
                string? consoleUser = null;
                foreach (var explorer in Process.GetProcessesByName("explorer"))
                {
                    try
                    {
                        if (explorer.SessionId == 0 || !TryReadOwner(explorer, out var account))
                        {
                            continue;
                        }

                        if (explorer.SessionId == currentSession)
                        {
                            currentUser ??= account;
                        }
                        else if (explorer.SessionId == consoleSession)
                        {
                            consoleUser ??= account;
                        }
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
                    {
                    }
                    finally
                    {
                        explorer.Dispose();
                    }
                }

                if (currentUser is not null)
                {
                    return currentUser;
                }

                if (consoleUser is not null)
                {
                    return consoleUser;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
            }

            return WindowsIdentity.GetCurrent().Name;
        }

        private static int ConsoleSessionId()
        {
            var session = Native.WTSGetActiveConsoleSessionId();
            return session is 0 or 0xFFFFFFFF ? -1 : unchecked((int)session);
        }

        private static bool TryReadOwner(Process process, out string account)
        {
            account = "";
            var handle = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, (uint)process.Id);
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            var token = IntPtr.Zero;
            var buffer = IntPtr.Zero;
            try
            {
                if (!Native.OpenProcessToken(handle, Native.TokenQuery, out token))
                {
                    return false;
                }

                Native.GetTokenInformation(token, Native.TokenUser, IntPtr.Zero, 0, out var needed);
                if (needed <= 0)
                {
                    return false;
                }

                buffer = Marshal.AllocHGlobal(needed);
                if (!Native.GetTokenInformation(token, Native.TokenUser, buffer, needed, out _))
                {
                    return false;
                }

                var sid = Marshal.ReadIntPtr(buffer);
                account = new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
                return !string.IsNullOrWhiteSpace(account);
            }
            catch (Exception exception) when (exception is IdentityNotMappedException or ArgumentException or OverflowException)
            {
                return false;
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }

                if (token != IntPtr.Zero)
                {
                    Native.CloseHandle(token);
                }

                Native.CloseHandle(handle);
            }
        }
    }

    private static class Native
    {
        internal const uint ProcessQueryLimitedInformation = 0x1000;
        internal const uint TokenQuery = 0x0008;
        internal const int TokenUser = 1;

        [DllImport("kernel32.dll")]
        internal static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation, int tokenInformationLength, out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);
    }
}
