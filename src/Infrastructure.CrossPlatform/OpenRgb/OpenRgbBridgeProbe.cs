using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;

namespace RigForge.Infrastructure.CrossPlatform.OpenRgb;

/// <summary>
/// Opt-in OpenRGB SDK client. Disabled unless <c>RIGFORGE_OPENRGB_ENDPOINT</c> is set
/// or <c>Hardware:OpenRgb:Enabled</c> is true. A closed port yields an empty device list.
/// </summary>
internal sealed partial class OpenRgbBridgeProbe(
    IConfiguration configuration,
    IOptions<HardwareOptions> options,
    ILogger<OpenRgbBridgeProbe> logger) : IDeviceProbe
{
    public string Name => "OpenRGB SDK";

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken) =>
        ProbeAsync([], cancellationToken);

    public async Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(
        IReadOnlyList<IHardwareDevice> existingDevices,
        CancellationToken cancellationToken)
    {
        if (existingDevices.Count > 0)
        {
            return existingDevices;
        }

        if (!OpenRgbProtocol.TryGetEndpoint(new ConfigurationView(configuration), out var host, out var port))
        {
            return [];
        }

        try
        {
            var devices = await OpenRgbBridgeDevice.ConnectAllAsync(host, port, options.Value.DryRun, cancellationToken).ConfigureAwait(false);
            if (devices.Count > 0)
            {
                LogConnected(host, port, devices.Count);
            }

            return devices;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogUnavailable(ex, host, port);
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "OpenRGB SDK bridge connected to {Host}:{Port} with {Controllers} controller(s)")]
    private partial void LogConnected(string host, int port, int controllers);

    [LoggerMessage(Level = LogLevel.Debug, Message = "OpenRGB SDK bridge at {Host}:{Port} is not available")]
    private partial void LogUnavailable(Exception exception, string host, int port);
}
