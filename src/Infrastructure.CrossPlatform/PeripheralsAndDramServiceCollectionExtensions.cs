using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RigForge.Core.Interfaces;
using RigForge.Infrastructure.CrossPlatform.Discovery;
using RigForge.Infrastructure.CrossPlatform.Dram;
using RigForge.Infrastructure.CrossPlatform.OpenRgb;
using RigForge.Infrastructure.CrossPlatform.Peripherals.Logitech;
using RigForge.Infrastructure.CrossPlatform.Peripherals.QmkVial;
using RigForge.Infrastructure.CrossPlatform.Peripherals.Razer;

namespace RigForge.Infrastructure.CrossPlatform;

/// <summary>
/// Registers peripheral, DRAM, OpenRGB, and unmanaged-discovery probes.
/// Wave 5 calls this from the cross-platform DI setup.
/// </summary>
internal static class PeripheralsAndDramServiceCollectionExtensions
{
    public static IServiceCollection AddPeripheralsDramAndDiscoveryProbes(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IDeviceProbe, QmkVialProbe>();
        services.AddSingleton<IDeviceProbe, LogitechHidPpProbe>();
        services.AddSingleton<IDeviceProbe, RazerChromaProbe>();
        services.AddSingleton<IDeviceProbe, EneDramProbe>();
        services.AddSingleton<IDeviceProbe, CorsairDramProbe>();
        services.AddSingleton<IDeviceProbe>(sp => new OpenRgbBridgeProbe(
            configuration,
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HardwareOptions>>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OpenRgbBridgeProbe>>()));
        services.AddSingleton<IDeviceProbe, UnmanagedRgbControllerProbe>();
        return services;
    }
}
