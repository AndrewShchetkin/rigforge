using System.Runtime.Versioning;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Hosting.WindowsServices;
using RigForge.Host.Daemon.Hosting;

namespace RigForge.Host.Daemon.Tray;

/// <summary>
/// Shows the tray icon while the daemon runs in an interactive Windows session.
/// A session-0 service and Linux keep running without one.
/// </summary>
internal sealed partial class TrayIconService(IServer server, IHostApplicationLifetime lifetime, IAutostartService autostart, ILoggerFactory loggerFactory, ILogger<TrayIconService> logger) : IHostedService
{
    private IDisposable? _tray;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            StartWindowsTray();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _tray?.Dispose();
        _tray = null;
        return Task.CompletedTask;
    }

    [SupportedOSPlatform("windows")]
    private void StartWindowsTray()
    {
        if (!TrayIconPolicy.ShouldShow(isWindows: true, WindowsServiceHelpers.IsWindowsService()))
        {
            return;
        }

        var launcher = new ElectronDashboardLauncher(loggerFactory.CreateLogger<ElectronDashboardLauncher>());
        var icon = new WindowsTrayIcon(() => OpenDashboard(launcher), RequestExit, autostart, loggerFactory.CreateLogger<WindowsTrayIcon>());
        icon.Start();
        _tray = icon;
    }

    [SupportedOSPlatform("windows")]
    private DashboardLaunch OpenDashboard(ElectronDashboardLauncher launcher)
    {
        try
        {
            var executable = TrayIconPolicy.FindShellExecutable(
                TrayIconPolicy.ShellCandidates(AppContext.BaseDirectory, TrayIconPolicy.FindRepositoryRoot(AppContext.BaseDirectory)),
                File.Exists);
            if (executable is null)
            {
                LogMissingShell(AppContext.BaseDirectory);
                return DashboardLaunch.Missing;
            }

            var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
            return launcher.Launch(executable, TrayIconPolicy.DashboardUrl(addresses));
        }
        catch (Exception exception)
        {
            LogOpenFailed(exception.Message);
            return DashboardLaunch.Rejected;
        }
    }

    private void RequestExit()
    {
        ThreadPool.QueueUserWorkItem(static state => ((IHostApplicationLifetime)state!).StopApplication(), lifetime);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "RigForge window is not installed next to {DaemonDirectory} or in the repository shell output")]
    private partial void LogMissingShell(string daemonDirectory);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not open the RigForge window: {Error}")]
    private partial void LogOpenFailed(string error);
}
