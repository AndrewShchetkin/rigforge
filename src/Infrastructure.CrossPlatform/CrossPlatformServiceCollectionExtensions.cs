using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RigForge.Core.Interfaces;
using RigForge.Infrastructure.CrossPlatform.Asus;
using RigForge.Infrastructure.CrossPlatform.Kingston;
using RigForge.Infrastructure.CrossPlatform.Nvidia;
using RigForge.Infrastructure.CrossPlatform.Nzxt;
using RigForge.Infrastructure.CrossPlatform.Persistence;
using RigForge.Infrastructure.CrossPlatform.Platform;
using RigForge.Infrastructure.CrossPlatform.Smbus;
using RigForge.Infrastructure.CrossPlatform.Usb;

namespace RigForge.Infrastructure.CrossPlatform;

public static class CrossPlatformServiceCollectionExtensions
{
    /// <summary>
    /// Registers the USB/NVML drivers and portable defaults. Platform modules (Windows/Linux) are registered
    /// afterwards and replace the defaults (precision timer, screen capture, bulk transport).
    /// </summary>
    public static IServiceCollection AddRigForgeCrossPlatformInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<HardwareOptions>(configuration.GetSection(HardwareOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));

        services.TryAddSingleton<IUsbBulkTransportFactory, LibUsbBulkTransportFactory>();
        services.TryAddSingleton<ISmbusControllerFactory, NullSmbusControllerFactory>();
        services.TryAddSingleton<IPrecisionTimer, SleepPrecisionTimer>();
        services.TryAddSingleton<IScreenCapturer, NullScreenCapturer>();
        services.TryAddSingleton<IProfileStore, JsonProfileStore>();

        services.AddSingleton<IDeviceProbe, NzxtDeviceProbe>();
        services.AddSingleton<IDeviceProbe, AsusAuraProbe>();
        services.AddSingleton<IDeviceProbe, KingstonFuryProbe>();
        services.AddSingleton<IDeviceProbe, NvmlProbe>();

        return services;
    }
}
