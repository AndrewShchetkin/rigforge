using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace RigForge.Host.Daemon.Tray;

internal enum DashboardLaunch
{
    Started,
    Missing,
    Rejected,
}

/// <summary>
/// Starts <c>RigForge.exe</c> on the dashboard URL. The logon task is elevated; the window is started
/// with Explorer's token so it runs at the interactive user's integrity level.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class ElectronDashboardLauncher(ILogger<ElectronDashboardLauncher> logger)
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const uint TokenDuplicate = 0x0002;
    private const uint TokenAssignPrimary = 0x0001;
    private const uint TokenAdjustDefault = 0x0080;
    private const uint TokenAdjustSessionId = 0x0100;
    private const uint SecurityImpersonation = 2;
    private const uint TokenPrimary = 1;
    private const uint LogonWithProfile = 0x00000001;

    public DashboardLaunch Launch(string executable, string dashboardUrl)
    {
        if (TrayIconPolicy.TryNormalizeDashboardUrl(dashboardUrl) is not { } url)
        {
            LogRejected(dashboardUrl);
            return DashboardLaunch.Rejected;
        }

        if (executable.Contains('"', StringComparison.Ordinal) || !File.Exists(executable))
        {
            LogMissing(executable);
            return DashboardLaunch.Missing;
        }

        if (IsElevated() && TryLaunchAtMediumIntegrity(executable, url))
        {
            LogOpening(url);
            return DashboardLaunch.Started;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                ArgumentList = { url },
            });
            LogOpening(url);
            return DashboardLaunch.Started;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            LogStartFailed(exception.Message);
            return DashboardLaunch.Rejected;
        }
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private bool TryLaunchAtMediumIntegrity(string executable, string url)
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        var launched = false;
        foreach (var explorer in Process.GetProcessesByName("explorer"))
        {
            try
            {
                if (!launched && explorer.SessionId == sessionId && LaunchWithProcessToken(explorer, executable, url))
                {
                    launched = true;
                }
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                LogElevationFallback(exception.Message);
            }
            finally
            {
                explorer.Dispose();
            }
        }

        return launched;
    }

    private bool LaunchWithProcessToken(Process explorer, string executable, string url)
    {
        var processHandle = Native.OpenProcess(ProcessQueryLimitedInformation, false, (uint)explorer.Id);
        if (processHandle == IntPtr.Zero)
        {
            return false;
        }

        var token = IntPtr.Zero;
        var primary = IntPtr.Zero;
        try
        {
            const uint access = TokenAssignPrimary | TokenDuplicate | TokenQuery | TokenAdjustDefault | TokenAdjustSessionId;
            if (!Native.OpenProcessToken(processHandle, TokenDuplicate | TokenQuery, out token)
                || !Native.DuplicateTokenEx(token, access, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out primary))
            {
                LogElevationFallback(Marshal.GetLastWin32Error().ToString());
                return false;
            }

            var directory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory;
            if (TryCreateProcess(primary, LogonWithProfile, executable, url, directory, out _)
                || TryCreateProcess(primary, 0, executable, url, directory, out var error))
            {
                return true;
            }

            LogElevationFallback(error.ToString());
            return false;
        }
        finally
        {
            if (primary != IntPtr.Zero)
            {
                Native.CloseHandle(primary);
            }

            if (token != IntPtr.Zero)
            {
                Native.CloseHandle(token);
            }

            Native.CloseHandle(processHandle);
        }
    }

    private static bool TryCreateProcess(IntPtr token, uint logonFlags, string executable, string url, string directory, out int error)
    {
        error = 0;
        var commandLine = Marshal.StringToHGlobalUni($"\"{executable}\" \"{url}\"");
        try
        {
            var startup = new Native.StartupInfo { cb = Marshal.SizeOf<Native.StartupInfo>() };
            if (!Native.CreateProcessWithTokenW(token, logonFlags, executable, commandLine, 0, IntPtr.Zero, directory, ref startup, out var information))
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }

            Native.CloseHandle(information.hThread);
            Native.CloseHandle(information.hProcess);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(commandLine);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Opening the RigForge window at {Url}")]
    private partial void LogOpening(string url);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused to open a dashboard URL that is not loopback http: {Url}")]
    private partial void LogRejected(string url);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RigForge window is not installed. Looked for {Path}")]
    private partial void LogMissing(string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not drop elevation for the RigForge window ({Error}); starting it in this process's integrity level")]
    private partial void LogElevationFallback(string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not start the RigForge window: {Error}")]
    private partial void LogStartFailed(string error);

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DuplicateTokenEx(
            IntPtr existingToken,
            uint desiredAccess,
            IntPtr tokenAttributes,
            uint impersonationLevel,
            uint tokenType,
            out IntPtr newToken);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcessWithTokenW(
            IntPtr token,
            uint logonFlags,
            string applicationName,
            IntPtr commandLine,
            uint creationFlags,
            IntPtr environment,
            string currentDirectory,
            ref StartupInfo startupInfo,
            out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct StartupInfo
        {
            public int cb;
            public IntPtr lpReserved;
            public IntPtr lpDesktop;
            public IntPtr lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ProcessInformation
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }
    }
}
