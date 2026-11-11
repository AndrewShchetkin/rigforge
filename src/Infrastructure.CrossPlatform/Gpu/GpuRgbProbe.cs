using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Gpu.Asus;
using RigForge.Infrastructure.CrossPlatform.Gpu.Gigabyte;
using RigForge.Infrastructure.CrossPlatform.Gpu.Msi;

namespace RigForge.Infrastructure.CrossPlatform.Gpu;

/// <summary>
/// Detects ASUS Aura, MSI Mystic Light and Gigabyte RGB Fusion GPU controllers on the GPUs' own I2C buses.
/// Cards are matched by PCI subsystem identity (<see cref="GpuPciCatalog"/>); unlisted ASUS cards are accepted only
/// after a read-only <c>AUMA</c>/<c>AURA</c> controller-name check. Rescans keep already open devices and never
/// touch the I2C registers of a card that is already managed.
/// </summary>
internal sealed partial class GpuRgbProbe(
    IGpuI2cBusFactory busFactory,
    IOptions<HardwareOptions> options,
    ILogger<GpuRgbProbe> logger,
    IPrecisionTimer timer,
    IDeviceManifestCatalog? manifests = null) : IDeviceProbe
{
    private const string DefaultAsusName = "ASUS Aura GPU";

    public string Name => "GPU I2C RGB";

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken) =>
        ProbeAsync([], cancellationToken);

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(
        IReadOnlyList<IHardwareDevice> existingDevices,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.GpuRgb.Enabled)
        {
            return Task.FromResult<IReadOnlyList<IHardwareDevice>>([]);
        }

        if (!busFactory.IsAvailable)
        {
            LogBackendUnavailable(busFactory.BackendName, busFactory.DiagnosticStatus);
            return Task.FromResult<IReadOnlyList<IHardwareDevice>>([]);
        }

        var devices = new List<IHardwareDevice>();
        var buses = busFactory.OpenBuses();
        for (var index = 0; index < buses.Count; index++)
        {
            var bus = buses[index];
            var keepBus = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                keepBus = ProbeBus(bus, existingDevices, settings.DryRun, devices);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogBusProbeFailed(ex, bus.Location);
            }
            finally
            {
                if (!keepBus)
                {
                    bus.Dispose();
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                DisposeRemaining(buses, index + 1);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        return Task.FromResult<IReadOnlyList<IHardwareDevice>>(devices);
    }

    /// <summary>Returns true when ownership of <paramref name="bus"/> moved to a newly opened device.</summary>
    private bool ProbeBus(IGpuI2cBus bus, IReadOnlyList<IHardwareDevice> existingDevices, bool dryRun, List<IHardwareDevice> devices)
    {
        var pci = bus.PciId;
        var candidates = GpuPciCatalog.FindMatches(pci, manifests);
        if (candidates.Count > 0)
        {
            foreach (var candidate in candidates)
            {
                var prefix = GpuDeviceIds.PrefixFor(candidate.Protocol);
                if (prefix is not null
                    && FindExisting(existingDevices, GpuDeviceIds.Create(prefix, bus.BusId, candidate.I2cAddress)) is { } existing)
                {
                    devices.Add(existing);
                    LogKeptExisting(existing.Id);
                    return false;
                }
            }

            foreach (var candidate in candidates)
            {
                if (TryOpenCandidate(bus, candidate, dryRun) is { } device)
                {
                    devices.Add(device);
                    LogDetected(device.Name, device.Id, bus.Location);
                    return true;
                }
            }

            LogNoCandidateResponded(GpuDeviceIds.FormatPciId(pci), bus.Location);
            return false;
        }

        if (pci.VendorId == GpuPciIdentity.NvidiaVendorId && pci.SubVendorId == GpuPciIdentity.AsusSubVendorId)
        {
            return ProbeUnlistedAsus(bus, existingDevices, dryRun, devices);
        }

        LogUnsupportedCard(GpuDeviceIds.FormatPciId(pci));
        return false;
    }

    private bool ProbeUnlistedAsus(IGpuI2cBus bus, IReadOnlyList<IHardwareDevice> existingDevices, bool dryRun, List<IHardwareDevice> devices)
    {
        var expectedId = GpuDeviceIds.Create(GpuDeviceIds.AsusPrefix, bus.BusId, AsusAuraGpuProtocol.DefaultSlaveAddress);
        if (FindExisting(existingDevices, expectedId) is { } existing)
        {
            devices.Add(existing);
            LogKeptExisting(existing.Id);
            return false;
        }

        // Read-only identification: the controller name is read, nothing is written until the signature matches.
        if (!AsusAuraGpuProtocol.TryReadControllerName(bus, AsusAuraGpuProtocol.DefaultSlaveAddress, out var controllerName)
            || !AsusAuraGpuProtocol.IsAuraGpuSignature(controllerName))
        {
            LogSignatureRejected(GpuDeviceIds.FormatPciId(bus.PciId), controllerName);
            return false;
        }

        var name = string.IsNullOrWhiteSpace(bus.AdapterName) ? DefaultAsusName : bus.AdapterName;
        var entry = new GpuCatalogEntry(
            bus.PciId,
            name,
            "ASUS",
            DeviceProtocolFamily.AsusAuraGpuI2c,
            AsusAuraGpuProtocol.DefaultSlaveAddress);
        if (AsusAuraGpuDevice.TryOpen(bus, entry, dryRun) is not { } device)
        {
            return false;
        }

        devices.Add(device);
        LogDetected(device.Name, device.Id, bus.Location);
        return true;
    }

    private IRgbDevice? TryOpenCandidate(IGpuI2cBus bus, GpuCatalogEntry candidate, bool dryRun)
    {
        try
        {
            return candidate.Protocol switch
            {
                DeviceProtocolFamily.AsusAuraGpuI2c => AsusAuraGpuDevice.TryOpen(bus, candidate, dryRun),
                DeviceProtocolFamily.MsiMysticGpuI2c => MsiMysticGpuDevice.TryOpen(bus, candidate, dryRun),
                DeviceProtocolFamily.GigabyteFusionGpuI2c => GigabyteFusionGpuDevice.TryOpen(bus, candidate, dryRun, timer),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCandidateFailed(ex, candidate.Name, candidate.I2cAddress);
            return null;
        }
    }

    private static IHardwareDevice? FindExisting(IReadOnlyList<IHardwareDevice> existingDevices, string id)
    {
        for (var i = 0; i < existingDevices.Count; i++)
        {
            if (string.Equals(existingDevices[i].Id, id, StringComparison.Ordinal))
            {
                return existingDevices[i];
            }
        }

        return null;
    }

    private static void DisposeRemaining(IReadOnlyList<IGpuI2cBus> buses, int from)
    {
        for (var i = from; i < buses.Count; i++)
        {
            buses[i].Dispose();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "GPU I2C backend {Backend} is unavailable: {Reason}")]
    private partial void LogBackendUnavailable(string backend, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Detected {Name} ({Id}) on {BusLocation}")]
    private partial void LogDetected(string name, string id, string busLocation);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Keeping already open GPU RGB device {Id}; skipping I2C access")]
    private partial void LogKeptExisting(string id);

    [LoggerMessage(Level = LogLevel.Debug, Message = "GPU {PciId} on {BusLocation} is in the catalog but no RGB controller candidate responded")]
    private partial void LogNoCandidateResponded(string pciId, string busLocation);

    [LoggerMessage(Level = LogLevel.Debug, Message = "GPU {PciId} is not a supported RGB card; ignoring")]
    private partial void LogUnsupportedCard(string pciId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Unlisted ASUS GPU {PciId} rejected: controller name '{ControllerName}' is not an Aura GPU signature (no writes were sent)")]
    private partial void LogSignatureRejected(string pciId, string controllerName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "GPU RGB candidate {Name} at 0x{Address:X2} failed to open")]
    private partial void LogCandidateFailed(Exception exception, string name, byte address);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Probing GPU I2C bus {BusLocation} failed")]
    private partial void LogBusProbeFailed(Exception exception, string busLocation);
}
