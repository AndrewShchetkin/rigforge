using Microsoft.Extensions.Configuration;

namespace RigForge.Infrastructure.CrossPlatform.OpenRgb;

/// <summary>Adapts <see cref="IConfiguration"/> to the protocol's key lookup.</summary>
internal sealed class ConfigurationView(IConfiguration configuration) : IConfigurationView
{
    public string? this[string key] => configuration[key];
}
