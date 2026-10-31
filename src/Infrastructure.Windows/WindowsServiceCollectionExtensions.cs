using System.Runtime.Versioning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RigForge.Core.Interfaces;
using RigForge.Infrastructure.CrossPlatform.Smbus;
using RigForge.Infrastructure.CrossPlatform.Usb;
using RigForge.Infrastructure.Windows.Capture;
using RigForge.Infrastructure.Windows.Hardware;
using RigForge.Infrastructure.Windows.Platform;
using RigForge.Infrastructure.Windows.Smbus;
using RigForge.Infrastructure.Windows.Usb;

namespace RigForge.Infrastructure.Windows;

public static class WindowsServiceCollectionExtensions
{
    /// <summary>Registers the Windows implementations; call after AddRigForgeCrossPlatformInfrastructure.</summary>
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddRigForgeWindowsPlatform(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WindowsPlatformOptions>(configuration.GetSection(WindowsPlatformOptions.SectionName));

        services.Replace(ServiceDescriptor.Singleton<IPrecisionTimer, WaitableTimerPrecisionTimer>());
        services.Replace(ServiceDescriptor.Singleton<IScreenCapturer, DxgiScreenCapturer>());
        services.Replace(ServiceDescriptor.Singleton<IUsbBulkTransportFactory, WinUsbBulkTransportFactory>());
        services.Replace(ServiceDescriptor.Singleton<ISmbusControllerFactory, PawnIoSmbusControllerFactory>());
        services.AddSingleton<IDeviceProbe, WindowsHardwareProbe>();

        return services;
    }
}
