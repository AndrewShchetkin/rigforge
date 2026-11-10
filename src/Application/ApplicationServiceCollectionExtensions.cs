using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RigForge.Application.AioDisplay;
using RigForge.Application.Diagnostics;
using RigForge.Application.Cooling;
using RigForge.Application.Devices;
using RigForge.Application.Effects;
using RigForge.Application.Layout;
using RigForge.Application.Profiles;
using RigForge.Application.Telemetry;
using RigForge.Application.Validation;
using RigForge.Core.Models;

namespace RigForge.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>Registers the engines and services. Options are bound by the host.</summary>
    public static IServiceCollection AddRigForgeApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<DeviceManager>();
        services.AddSingleton<ProfileService>();
        services.AddSingleton<TelemetryService>();
        services.AddSingleton<CanvasLayoutService>();
        services.AddSingleton<EffectRegistry>();
        services.AddSingleton<EffectRenderEngine>();
        services.AddSingleton<PerformanceMonitor>();
        services.AddSingleton<FanControlEngine>();
        services.AddSingleton<LcdRenderEngine>();
        services.AddSingleton<IValidator<RigProfile>, RigProfileValidator>();

        return services;
    }
}
