using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;
using RigForge.Infrastructure.CrossPlatform.Smbus;

namespace RigForge.Infrastructure.CrossPlatform.Dram;

/// <summary>
/// Corsair Vengeance / Dominator RGB DRAM probe.
/// Part numbers (CMH, CMW, CMT, CMP, DOMINATOR) from SMBIOS or cached sysfs SPD / udev / EDAC gate the bus.
/// Only <c>0x58..0x5F</c> is touched. <c>Hardware:CorsairDram:RequireSmbiosMatch: false</c> is an explicit opt-in.
/// </summary>
internal sealed partial class CorsairDramProbe(
    ISmbusControllerFactory smbusFactory,
    IOptions<HardwareOptions> options,
    ILogger<CorsairDramProbe> logger,
    IConfiguration? configuration = null) : IDeviceProbe
{
    public string Name => "Corsair RGB DRAM (SMBus)";

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(
        IReadOnlyList<IHardwareDevice> existingDevices,
        CancellationToken cancellationToken) =>
        existingDevices.Count > 0
            ? Task.FromResult(existingDevices)
            : ProbeAsync(cancellationToken);

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken)
    {
        if (string.Equals(configuration?["Hardware:CorsairDram:Enabled"], "false", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IReadOnlyList<IHardwareDevice>>([]);
        }

        var installed = smbusFactory.GetInstalledMemory();
        var matched = installed.Where(static stick => stick.IsCorsairDram).ToArray();
        var requireMatch = !string.Equals(configuration?["Hardware:CorsairDram:RequireSmbiosMatch"], "false", StringComparison.OrdinalIgnoreCase);
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

        var leds = matched.Length == 0
            ? CorsairDramProtocol.LedsPerDimm
            : matched.Max(static stick => CorsairDramProtocol.LedCountForPart(stick.PartNumber));
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
                var detected = new List<CorsairSlotProbeResult>();
                for (var slot = 0; slot < CorsairDramProtocol.MaxSlots; slot++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = CorsairDramProtocol.ProbeSlot(controller, slot);
                    if (result.Status == CorsairSlotProbeStatus.Supported)
                    {
                        detected.Add(result);
                        LogDetected(controller.Location, result.Address);
                    }
                    else if (result.Status == CorsairSlotProbeStatus.SignatureMismatch)
                    {
                        LogSignatureRejected(controller.Location, result.Address, result.Vendor, result.Model);
                    }
                }

                if (detected.Count > 0)
                {
                    devices.Add(new CorsairDramDevice(controller, detected, matched, leds, dryRun));
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "No Corsair RGB DRAM module identity; skipping SMBus probe")]
    private partial void LogNoSmbiosMatch();

    [LoggerMessage(Level = LogLevel.Information, Message = "Corsair RGB DRAM is installed, but SMBus backend ({Backend}) is unavailable: {Reason}")]
    private partial void LogBackendUnavailable(string backend, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Detected Corsair RGB DRAM on {BusLocation} at 0x{Address:X2}")]
    private partial void LogDetected(string busLocation, byte address);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rejected Corsair id vendor 0x{Vendor:X2} model 0x{Model:X2} on {BusLocation} at 0x{Address:X2}")]
    private partial void LogSignatureRejected(string busLocation, byte address, byte vendor, byte model);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Probing Corsair DRAM on SMBus {BusLocation} failed")]
    private partial void LogProbeFailed(Exception exception, string busLocation);
}
