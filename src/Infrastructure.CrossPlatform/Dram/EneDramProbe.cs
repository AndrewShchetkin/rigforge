using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;
using RigForge.Infrastructure.CrossPlatform.Smbus;

namespace RigForge.Infrastructure.CrossPlatform.Dram;

/// <summary>
/// ENE RGB DRAM probe. Module identity (SMBIOS Type 17 or cached sysfs SPD / udev / EDAC) is checked first.
/// SMBus stays closed until a matching module is known, unless <c>Hardware:EneDram:RequireSmbiosMatch</c> is false.
/// Addresses stay inside the ENE whitelist.
/// </summary>
internal sealed partial class EneDramProbe(
    ISmbusControllerFactory smbusFactory,
    IOptions<HardwareOptions> options,
    ILogger<EneDramProbe> logger,
    IConfiguration? configuration = null) : IDeviceProbe
{
    public string Name => "ENE RGB DRAM (SMBus)";

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(
        IReadOnlyList<IHardwareDevice> existingDevices,
        CancellationToken cancellationToken) =>
        existingDevices.Count > 0
            ? Task.FromResult(existingDevices)
            : ProbeAsync(cancellationToken);

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken)
    {
        if (string.Equals(configuration?["Hardware:EneDram:Enabled"], "false", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IReadOnlyList<IHardwareDevice>>([]);
        }

        var installed = smbusFactory.GetInstalledMemory();
        var matched = installed.Where(static stick => stick.IsEneCompatibleDram).ToArray();
        var requireMatch = !string.Equals(configuration?["Hardware:EneDram:RequireSmbiosMatch"], "false", StringComparison.OrdinalIgnoreCase);
        if (!SmbusProbePolicy.AllowsBusProbe(smbusFactory, requireMatch, matched.Length))
        {
            LogNoSmbiosMatch();
            return Task.FromResult<IReadOnlyList<IHardwareDevice>>([]);
        }

        if (!smbusFactory.IsAvailable)
        {
            LogBackendUnavailable(smbusFactory.BackendName, smbusFactory.DiagnosticStatus);
            return Task.FromResult<IReadOnlyList<IHardwareDevice>>([]);
        }

        var controllers = smbusFactory.OpenControllers();
        var devices = new List<IHardwareDevice>();
        var dryRun = options.Value.DryRun;
        foreach (var controller in controllers)
        {
            if (matched.Length > 0 && devices.OfType<IRgbDevice>().Sum(static device => device.Zones.Count) >= matched.Length)
            {
                controller.Dispose();
                continue;
            }

            var keep = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var detected = new List<EneSlotProbeResult>();
                foreach (var address in EneDramProtocol.ProbeAddresses)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = EneDramProtocol.ProbeAddress(controller, address);
                    if (result.Status == EneSlotProbeStatus.Supported)
                    {
                        detected.Add(result);
                        LogDetected(controller.Location, address, result.Signature ?? string.Empty);
                    }
                    else if (result.Status == EneSlotProbeStatus.SignatureMismatch)
                    {
                        LogSignatureRejected(controller.Location, address, result.Signature ?? string.Empty);
                    }
                }

                if (detected.Count > 0)
                {
                    devices.Add(new EneDramDevice(controller, detected, matched, EneDramProtocol.LedsPerDimm, dryRun));
                    keep = true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogProbeFailed(ex, controller.Location);
            }
            finally
            {
                if (!keep)
                {
                    controller.Dispose();
                }
            }
        }

        return Task.FromResult<IReadOnlyList<IHardwareDevice>>(devices);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "No ENE RGB DRAM module identity; skipping SMBus probe")]
    private partial void LogNoSmbiosMatch();

    [LoggerMessage(Level = LogLevel.Information, Message = "ENE RGB DRAM is installed, but SMBus backend ({Backend}) is unavailable: {Reason}")]
    private partial void LogBackendUnavailable(string backend, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Detected ENE DRAM '{Signature}' on {BusLocation} at 0x{Address:X2}")]
    private partial void LogDetected(string busLocation, byte address, string signature);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rejected non-ENE signature '{Signature}' on {BusLocation} at 0x{Address:X2}")]
    private partial void LogSignatureRejected(string busLocation, byte address, string signature);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Probing ENE DRAM on SMBus {BusLocation} failed")]
    private partial void LogProbeFailed(Exception exception, string busLocation);
}
