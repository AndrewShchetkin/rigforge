using System.Diagnostics;
using System.Net;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RigForge.Host.Daemon;
using RigForge.Host.Daemon.Api;
using RigForge.Host.Daemon.Contracts;
using RigForge.Host.Daemon.Hosting;
using RigForge.Host.Daemon.Services;

namespace RigForge.UnitTests.Host;

public sealed class AutostartAndShutdownTests
{
    private static readonly XNamespace TaskNs = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Fact]
    public void BuildXml_enables_the_logon_trigger_and_keeps_the_task_startable()
    {
        var xml = WindowsLogonTaskXml.BuildXml(
            @"C:\Program Files\RigForge\RigForge.Daemon.exe",
            @"C:\Program Files\RigForge",
            @"DESKTOP\Alice",
            logonTriggerEnabled: true);

        var task = XDocument.Parse(xml).Root;
        Assert.NotNull(task);
        Assert.Equal(TaskNs, task.Name.Namespace);
        Assert.Equal("1.2", (string?)task.Attribute("version"));
        Assert.Equal("RigForge: RGB, fan/pump and AIO screen daemon", Value(task, "RegistrationInfo", "Description"));
        Assert.Equal(
            "D:P(A;;FA;;;BA)(A;;FA;;;SY)(A;;FRFX;;;IU)(A;;FRFX;;;AU)",
            Value(task, "RegistrationInfo", "SecurityDescriptor"));
        Assert.Equal("true", Value(task, "Triggers", "LogonTrigger", "Enabled"));
        Assert.Equal(@"DESKTOP\Alice", Value(task, "Triggers", "LogonTrigger", "UserId"));
        Assert.Equal(@"DESKTOP\Alice", Value(task, "Principals", "Principal", "UserId"));
        Assert.Equal("InteractiveToken", Value(task, "Principals", "Principal", "LogonType"));
        Assert.Equal("HighestAvailable", Value(task, "Principals", "Principal", "RunLevel"));
        Assert.Equal("true", Value(task, "Settings", "Enabled"));
        Assert.Equal("true", Value(task, "Settings", "AllowStartOnDemand"));
        Assert.Equal("PT0S", Value(task, "Settings", "ExecutionTimeLimit"));
        Assert.Equal(@"C:\Program Files\RigForge\RigForge.Daemon.exe", Value(task, "Actions", "Exec", "Command"));
        Assert.Equal(@"C:\Program Files\RigForge", Value(task, "Actions", "Exec", "WorkingDirectory"));
        Assert.Equal("Author", (string?)task.Element(TaskNs + "Actions")?.Attribute("Context"));
    }

    [Fact]
    public void BuildXml_can_disable_only_the_logon_trigger()
    {
        var xml = WindowsLogonTaskXml.BuildXml(
            @"C:\RigForge\RigForge.Daemon.exe",
            @"C:\RigForge",
            @"DESKTOP\Alice",
            logonTriggerEnabled: false);

        var task = XDocument.Parse(xml).Root!;
        Assert.Equal("false", Value(task, "Triggers", "LogonTrigger", "Enabled"));
        Assert.Equal("true", Value(task, "Settings", "Enabled"));
        Assert.Equal("true", Value(task, "Settings", "AllowStartOnDemand"));
    }

    [Fact]
    public void BuildXml_escapes_paths_and_user_names()
    {
        var xml = WindowsLogonTaskXml.BuildXml(
            @"C:\Program Files\RigForge & Co\RigForge.Daemon.exe",
            @"C:\Program Files\RigForge & Co",
            @"DOMAIN\User<Admin>",
            logonTriggerEnabled: true);

        Assert.Contains("&amp;", xml, StringComparison.Ordinal);
        Assert.Contains("&lt;", xml, StringComparison.Ordinal);
        Assert.Contains("D:P(A;;FA;;;BA)(A;;FA;;;SY)(A;;FRFX;;;IU)(A;;FRFX;;;AU)", xml, StringComparison.Ordinal);

        var task = XDocument.Parse(xml).Root!;
        Assert.Equal(@"C:\Program Files\RigForge & Co\RigForge.Daemon.exe", Value(task, "Actions", "Exec", "Command"));
        Assert.Equal(@"C:\Program Files\RigForge & Co", Value(task, "Actions", "Exec", "WorkingDirectory"));
        Assert.Equal(@"DOMAIN\User<Admin>", Value(task, "Triggers", "LogonTrigger", "UserId"));
        Assert.Equal(@"DOMAIN\User<Admin>", Value(task, "Principals", "Principal", "UserId"));
    }

    [Fact]
    public void TryParse_reads_an_enabled_task_and_a_disabled_logon_trigger()
    {
        var enabled = WindowsLogonTaskXml.BuildXml(@"C:\RigForge\RigForge.Daemon.exe", @"C:\RigForge", @"DESKTOP\Alice", true);
        Assert.True(WindowsLogonTaskXml.TryParse(enabled, out var on));
        Assert.True(on.SettingsEnabled);
        Assert.True(on.LogonTriggerEnabled);
        Assert.True(on.SettingsEnabled && on.LogonTriggerEnabled);
        Assert.Equal(@"DESKTOP\Alice", on.UserId);
        Assert.Equal(@"C:\RigForge\RigForge.Daemon.exe", on.ExecutablePath);
        Assert.Equal(@"C:\RigForge", on.WorkingDirectory);

        var triggerOff = WindowsLogonTaskXml.BuildXml(@"C:\RigForge\RigForge.Daemon.exe", @"C:\RigForge", @"DESKTOP\Alice", false);
        Assert.True(WindowsLogonTaskXml.TryParse(triggerOff, out var off));
        Assert.True(off.SettingsEnabled);
        Assert.False(off.LogonTriggerEnabled);
        Assert.False(off.SettingsEnabled && off.LogonTriggerEnabled);
    }

    [Fact]
    public void TryParse_treats_a_disabled_task_as_not_autostarting()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>DESKTOP\Alice</UserId>
                </LogonTrigger>
              </Triggers>
              <Settings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>false</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>C:\RigForge\RigForge.Daemon.exe</Command>
                  <WorkingDirectory>C:\RigForge</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;

        Assert.True(WindowsLogonTaskXml.TryParse(xml, out var info));
        Assert.False(info.SettingsEnabled);
        Assert.True(info.LogonTriggerEnabled);
        Assert.False(info.SettingsEnabled && info.LogonTriggerEnabled);
        Assert.Equal(@"DESKTOP\Alice", info.UserId);
    }

    [Fact]
    public void TryParse_preserve_keeps_an_existing_trigger_and_defaults_to_enabled()
    {
        var disabled = WindowsLogonTaskXml.BuildXml(@"C:\RigForge\RigForge.Daemon.exe", @"C:\RigForge", @"DESKTOP\Alice", false);
        Assert.True(WindowsLogonTaskXml.TryParse(disabled, out var existing));

        Assert.False(WindowsLogonTaskXml.ResolveLogonTrigger("preserve", taskExists: true, existing.LogonTriggerEnabled));
        Assert.True(WindowsLogonTaskXml.ResolveLogonTrigger("preserve", taskExists: true, currentLogonTriggerEnabled: true));
        Assert.True(WindowsLogonTaskXml.ResolveLogonTrigger("preserve", taskExists: false, currentLogonTriggerEnabled: false));
        Assert.True(WindowsLogonTaskXml.ResolveLogonTrigger("true", taskExists: true, currentLogonTriggerEnabled: false));
        Assert.False(WindowsLogonTaskXml.ResolveLogonTrigger("false", taskExists: true, currentLogonTriggerEnabled: true));
    }

    [Fact]
    public void TryParse_rejects_malformed_xml()
    {
        Assert.False(WindowsLogonTaskXml.TryParse("<Task>", out _));
        Assert.False(WindowsLogonTaskXml.TryParse("", out _));
    }

    [Fact]
    public void Parse_reads_install_uninstall_autostart_and_shutdown_commands()
    {
        var install = DaemonCliHandler.Parse(["--install-task", "--autostart", "preserve", "--user", @"CONTOSO\Operator"]);
        var installCommand = Assert.IsType<DaemonCliHandler.InstallTaskCommand>(install.Command);
        Assert.Equal("preserve", installCommand.AutostartMode);
        Assert.Equal(@"CONTOSO\Operator", installCommand.User);
        Assert.Null(install.Error);

        var defaults = DaemonCliHandler.Parse(["--install-task"]);
        var defaultInstall = Assert.IsType<DaemonCliHandler.InstallTaskCommand>(defaults.Command);
        Assert.Equal("true", defaultInstall.AutostartMode);
        Assert.Null(defaultInstall.User);

        var uninstall = DaemonCliHandler.Parse(["--uninstall-task"]);
        Assert.IsType<DaemonCliHandler.UninstallTaskCommand>(uninstall.Command);

        var enable = DaemonCliHandler.Parse(["--set-autostart", "true"]);
        Assert.True(Assert.IsType<DaemonCliHandler.SetAutostartCommand>(enable.Command).Enabled);
        var disable = DaemonCliHandler.Parse(["--set-autostart", "false"]);
        Assert.False(Assert.IsType<DaemonCliHandler.SetAutostartCommand>(disable.Command).Enabled);

        var shutdown = DaemonCliHandler.Parse(["--shutdown", "--timeout-ms", "1500", "--url", "http://127.0.0.1:5757"]);
        var shutdownCommand = Assert.IsType<DaemonCliHandler.ShutdownCommand>(shutdown.Command);
        Assert.Equal(1500, shutdownCommand.TimeoutMs);
        Assert.Equal("http://127.0.0.1:5757", shutdownCommand.Url);

        var shutdownDefaults = Assert.IsType<DaemonCliHandler.ShutdownCommand>(DaemonCliHandler.Parse(["--shutdown"]).Command);
        Assert.Equal(6000, shutdownDefaults.TimeoutMs);
        Assert.Equal("http://127.0.0.1:5757", shutdownDefaults.Url);

        Assert.IsType<DaemonCliHandler.RunElevatedCommand>(DaemonCliHandler.Parse(["--run-elevated"]).Command);
        Assert.NotNull(DaemonCliHandler.Parse(["--run-elevated", "--shutdown"]).Error);

        var daemon = DaemonCliHandler.Parse(["--urls", "http://127.0.0.1:5757"]);
        Assert.Null(daemon.Command);
        Assert.Null(daemon.Error);
    }

    [Theory]
    [InlineData("Origin")]
    [InlineData("Referer")]
    [InlineData("Sec-Fetch-Site")]
    public async Task Shutdown_rejects_requests_that_carry_browser_headers(string header)
    {
        var context = CreateContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers[header] = "http://evil.example";
        var lifetime = new StopLifetime();

        var result = ApiEndpoints.RequestShutdown(context, lifetime);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(lifetime.StopRequested);
    }

    [Fact]
    public async Task Shutdown_rejects_a_non_loopback_caller()
    {
        var context = CreateContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("8.8.8.8");
        var lifetime = new StopLifetime();

        var result = ApiEndpoints.RequestShutdown(context, lifetime);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(lifetime.StopRequested);
    }

    [Fact]
    public async Task Shutdown_accepts_a_clean_loopback_request()
    {
        var context = CreateContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        var lifetime = new StopLifetime();

        var result = ApiEndpoints.RequestShutdown(context, lifetime);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        var deadline = Environment.TickCount64 + 2000;
        while (!lifetime.StopRequested && Environment.TickCount64 < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.True(lifetime.StopRequested);
    }

    [Fact]
    public async Task BuildInfo_reports_product_and_autostart()
    {
        var enabled = await BuildInfo(supported: true, autostartEnabled: true);
        Assert.Equal("RigForge", enabled.Product);
        Assert.True(enabled.AutostartSupported);
        Assert.True(enabled.AutostartEnabled);
        Assert.Equal(Process.GetCurrentProcess().SessionId, enabled.SessionId);

        var disabled = await BuildInfo(supported: false, autostartEnabled: false);
        Assert.Equal("RigForge", disabled.Product);
        Assert.False(disabled.AutostartSupported);
        Assert.False(disabled.AutostartEnabled);
    }

    private static DefaultHttpContext CreateContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
    }

    private static async Task<DaemonInfoDto> BuildInfo(bool supported, bool autostartEnabled)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddRigForgeDaemonServices(configuration);
        services.AddSingleton<IAutostartService>(new StubAutostart(supported, autostartEnabled));
        await using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<SystemStateBuilder>().BuildInfo();
    }

    private static string? Value(XElement root, params string[] path)
    {
        XElement? current = root;
        foreach (var name in path)
        {
            current = current?.Element(TaskNs + name);
        }

        return current?.Value;
    }

    private sealed class StubAutostart(bool supported, bool enabled) : IAutostartService
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public AutostartStatus GetStatus() => new(supported, enabled, supported);

        public Task<AutostartStatus> SetEnabledAsync(bool value, CancellationToken cancellationToken = default) =>
            Task.FromResult(GetStatus());
    }

    private sealed class StopLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public bool StopRequested { get; private set; }

        public CancellationToken ApplicationStarted => _started.Token;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication()
        {
            StopRequested = true;
            _stopping.Cancel();
        }
    }
}
