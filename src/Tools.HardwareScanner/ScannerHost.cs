using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RigForge.Core.Interfaces;
using RigForge.Infrastructure.CrossPlatform;
using RigForge.Infrastructure.Linux;
using RigForge.Infrastructure.Windows;

namespace RigForge.Tools.HardwareScanner;

/// <summary>Wires the same drivers as the daemon, always in dry-run mode.</summary>
internal static class ScannerHost
{
    public static ServiceProvider Build(bool verbose)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Hardware:DryRun"] = "true" })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging
            .AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            })
            .SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Warning));

        services.AddRigForgeCrossPlatformInfrastructure(configuration);
        if (OperatingSystem.IsWindows())
        {
            services.AddRigForgeWindowsPlatform(configuration);
        }
        else if (OperatingSystem.IsLinux())
        {
            services.AddRigForgeLinuxPlatform(configuration);
        }

        return services.BuildServiceProvider();
    }

    public static async Task<IReadOnlyList<IHardwareDevice>> ProbeAllAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var devices = new List<IHardwareDevice>();
        foreach (var probe in services.GetServices<IDeviceProbe>())
        {
            try
            {
                devices.AddRange(await probe.ProbeAsync(cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await Console.Error.WriteLineAsync($"{probe.Name}: {ex.Message}");
            }
        }

        return devices;
    }

    public static async Task ReleaseAsync(IEnumerable<IHardwareDevice> devices)
    {
        foreach (var device in devices)
        {
            await device.DisposeAsync();
        }
    }
}
