using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RigForge.Core.Interfaces;
using RigForge.Infrastructure.Linux.Capture;
using RigForge.Infrastructure.Linux.Hardware;

namespace RigForge.Infrastructure.Linux;

public static class LinuxServiceCollectionExtensions
{
    /// <summary>Registers the Linux implementations; call after AddRigForgeCrossPlatformInfrastructure.</summary>
    public static IServiceCollection AddRigForgeLinuxPlatform(this IServiceCollection services, IConfiguration configuration)
    {
        services.Replace(ServiceDescriptor.Singleton<IScreenCapturer, LinuxScreenCapturerStub>());
        services.AddSingleton<IDeviceProbe, SysfsHwmonProbe>();
        return services;
    }
}
