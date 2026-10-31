using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Runtime.Versioning;
using RigForge.Core.Interfaces;
using RigForge.Infrastructure.CrossPlatform.Smbus;
using RigForge.Infrastructure.Linux.Capture;
using RigForge.Infrastructure.Linux.Hardware;
using RigForge.Infrastructure.Linux.Smbus;

namespace RigForge.Infrastructure.Linux;

public static class LinuxServiceCollectionExtensions
{
    /// <summary>Registers the Linux implementations; call after AddRigForgeCrossPlatformInfrastructure.</summary>
    [SupportedOSPlatform("linux")]
    public static IServiceCollection AddRigForgeLinuxPlatform(this IServiceCollection services, IConfiguration configuration)
    {
        services.Replace(ServiceDescriptor.Singleton<IScreenCapturer, LinuxScreenCapturerStub>());
        services.Replace(ServiceDescriptor.Singleton<ISmbusControllerFactory, LinuxI2cSmbusControllerFactory>());
        services.AddSingleton<IDeviceProbe, SysfsHwmonProbe>();
        return services;
    }
}
