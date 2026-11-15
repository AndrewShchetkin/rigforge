using System.Runtime.Versioning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RigForge.Application;
using RigForge.Host.Daemon.Hosting;
using RigForge.Host.Daemon.Services;
using RigForge.Infrastructure.CrossPlatform;
using RigForge.Infrastructure.Linux;
using RigForge.Infrastructure.Windows;

namespace RigForge.Host.Daemon;

public static class DaemonServiceCollectionExtensions
{
    /// <summary>
    /// Registers the application, cross-platform drivers, and the OS platform module.
    /// Web hosting, SignalR, and the background workers stay in the executable entry point.
    /// </summary>
    public static IServiceCollection AddRigForgeDaemonServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DaemonOptions>(configuration.GetSection(DaemonOptions.SectionName));
        services.Configure<RenderingOptions>(configuration.GetSection(RenderingOptions.SectionName));
        services.Configure<TelemetryOptions>(configuration.GetSection(TelemetryOptions.SectionName));
        services.Configure<CoolingOptions>(configuration.GetSection(CoolingOptions.SectionName));
        services.Configure<LcdOptions>(configuration.GetSection(LcdOptions.SectionName));

        services.AddRigForgeApplication();
        services.AddRigForgeCrossPlatformInfrastructure(configuration);
        if (OperatingSystem.IsWindows())
        {
            services.AddRigForgeWindowsPlatform(configuration);
            AddWindowsAutostart(services);
        }
        else if (OperatingSystem.IsLinux())
        {
            services.AddRigForgeLinuxPlatform(configuration);
            services.AddSingleton<IAutostartService, LinuxAutostartService>();
        }
        else
        {
            services.AddSingleton<IAutostartService, NullAutostartService>();
        }

        services.AddSingleton<SystemStateBuilder>();
        return services;
    }

    /// <summary>Builds the daemon service graph so tests can resolve probes without opening a socket.</summary>
    public static IServiceProvider BuildDaemonProvider(IConfiguration? configuration = null)
    {
        configuration ??= new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddRigForgeDaemonServices(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    [SupportedOSPlatform("windows")]
    private static void AddWindowsAutostart(IServiceCollection services) =>
        services.AddSingleton<IAutostartService, WindowsAutostartService>();
}
