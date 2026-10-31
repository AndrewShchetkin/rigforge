using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;
using RigForge.Infrastructure.CrossPlatform.Smbus;

namespace RigForge.Infrastructure.CrossPlatform.Kingston;

/// <summary>
/// Safely detects Kingston FURY DDR5 RGB modules:
/// 1) checks SMBIOS Type 17 for <c>KF5*</c> part numbers before touching SMBus,
/// 2) probes only <c>0x60..0x63</c> (never SPD <c>0x50..0x57</c> or PMIC <c>0x48..0x4F</c>),
/// 3) verifies the <c>"FURY"</c> ASCII signature and supported DDR5 model byte (<c>0x06</c>).
/// </summary>
internal sealed partial class KingstonFuryProbe(
    ISmbusControllerFactory smbusFactory,
    IOptions<HardwareOptions> options,
    ILogger<KingstonFuryProbe> logger,
    IPrecisionTimer? precisionTimer = null,
    Action<TimeSpan>? delay = null) : IDeviceProbe
{
    private readonly Action<TimeSpan>? _delay = delay ?? (precisionTimer is not null ? precisionTimer.Sleep : null);

    public string Name => "Kingston FURY DDR5 DRAM (SMBus)";

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var furySettings = settings.KingstonFury;
        if (!furySettings.Enabled)
        {
            return Task.FromResult<IReadOnlyList<IHardwareDevice>>([]);
        }

        var installedMemory = smbusFactory.GetInstalledMemory();
        var kingstonDdr5Sticks = installedMemory.Where(static m => m.IsKingstonFuryDdr5).ToArray();
        if (furySettings.RequireSmbiosMatch && kingstonDdr5Sticks.Length == 0)
        {
            LogNoKingstonSmbios();
            return Task.FromResult<IReadOnlyList<IHardwareDevice>>([]);
        }

        if (!smbusFactory.IsAvailable)
        {
            LogBackendUnavailable(smbusFactory.BackendName, smbusFactory.DiagnosticStatus);
            return Task.FromResult<IReadOnlyList<IHardwareDevice>>([]);
        }

        var candidateSlots = NormalizeCandidateSlots(furySettings.CandidateSlots);
        if (candidateSlots.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<IHardwareDevice>>([]);
        }

        var maxAllowedAddress = KingstonFuryDramProtocol.GetSlotAddress(candidateSlots.Max());
        var controllers = smbusFactory.OpenControllers();
        var devices = new List<IHardwareDevice>();

        foreach (var controller in controllers)
        {
            if (kingstonDdr5Sticks.Length > 0
                && devices.OfType<IRgbDevice>().Sum(static d => d.Zones.Count) >= kingstonDdr5Sticks.Length)
            {
                controller.Dispose();
                continue;
            }

            var keepController = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var detectedSlots = new List<KingstonSlotProbeResult>();

                foreach (var slotIndex in candidateSlots)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var probeResult = KingstonFuryDramProtocol.ProbeSlot(controller, slotIndex, _delay, maxAllowedAddress);
                    switch (probeResult.Status)
                    {
                        case KingstonSlotProbeStatus.Supported:
                            detectedSlots.Add(probeResult);
                            LogSlotDetected(controller.Location, probeResult.Address, probeResult.ModelCode ?? 0, probeResult.Detail);
                            break;

                        case KingstonSlotProbeStatus.UnsupportedModel:
                            LogUnsupportedModel(controller.Location, probeResult.Address, probeResult.ModelCode ?? 0);
                            break;
                    }
                }

                if (detectedSlots.Count > 0)
                {
                    var device = new KingstonFuryDramDevice(
                        controller,
                        detectedSlots,
                        kingstonDdr5Sticks,
                        furySettings.MaxFramesPerSecond,
                        furySettings.BrightnessPercent,
                        maxAllowedAddress,
                        settings.DryRun,
                        _delay);
                    devices.Add(device);
                    keepController = true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogBusProbeFailed(ex, controller.Location);
            }
            finally
            {
                if (!keepController)
                {
                    controller.Dispose();
                }
            }
        }

        return Task.FromResult<IReadOnlyList<IHardwareDevice>>(devices);
    }

    internal static int[] NormalizeCandidateSlots(int[]? slots)
    {
        if (slots is null || slots.Length == 0)
        {
            return [0, 1, 2, 3];
        }

        return slots
            .Where(static s => s is >= 0 and < KingstonFuryDramProtocol.MaxSupportedSlots)
            .Distinct()
            .Order()
            .ToArray();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "SMBIOS reports no Kingston DDR5 (KF5*) modules; skipping SMBus probe")]
    private partial void LogNoKingstonSmbios();

    [LoggerMessage(Level = LogLevel.Information, Message = "Kingston FURY DDR5 detected in SMBIOS, but SMBus backend ({Backend}) is unavailable: {Reason}")]
    private partial void LogBackendUnavailable(string backend, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Detected {Detail} on {BusLocation} at 0x{Address:X2} (model 0x{ModelCode:X2})")]
    private partial void LogSlotDetected(string busLocation, byte address, byte modelCode, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kingston FURY signature found on {BusLocation} at 0x{Address:X2}, but model code 0x{ModelCode:X2} is not in the supported DDR5 whitelist")]
    private partial void LogUnsupportedModel(string busLocation, byte address, byte modelCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Probing Kingston FURY DRAM on SMBus {BusLocation} failed")]
    private partial void LogBusProbeFailed(Exception exception, string busLocation);
}
