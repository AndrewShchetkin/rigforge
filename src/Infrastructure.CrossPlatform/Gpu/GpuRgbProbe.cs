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
/// after a read-only ENE presence check and the <c>AUMA</c>/<c>AURA</c> controller-name check (no mode/color writes; register-pointer select only), on desktop GPUs only. Rescans keep already open devices and never
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

    private readonly HashSet<string> _rejected = new(StringComparer.Ordinal);

    public string Name => "GPU I2C RGB";

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken) =>
        ProbeAsync([], cancellationToken);

    public async Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(
        IReadOnlyList<IHardwareDevice> existingDevices,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.GpuRgb.Enabled)
        {
            return [];
        }

        if (!busFactory.IsAvailable)
        {
            LogBackendUnavailable(busFactory.BackendName, busFactory.DiagnosticStatus);
            return [];
        }

        var devices = new List<IHardwareDevice>();

        // Preferred path: list adapters without opening them, so managed and previously rejected buses are never reopened.
        var descriptors = busFactory.EnumerateAdapters();
        if (descriptors.Count > 0)
        {
            return await ProbeEnumeratedAsync(descriptors, existingDevices, settings.DryRun, devices, cancellationToken).ConfigureAwait(false);
        }

        var buses = busFactory.OpenBuses();
        var index = 0;
        try
        {
            for (; index < buses.Count; index++)
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

                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
            // The caller never receives this scan's result, so nothing else would release what it opened.
            DisposeRemaining(buses, index + 1);
            await DisposeNewDevicesAsync(devices, existingDevices).ConfigureAwait(false);
            throw;
        }

        return devices;
    }

    private async Task<IReadOnlyList<IHardwareDevice>> ProbeEnumeratedAsync(
        IReadOnlyList<GpuBusDescriptor> descriptors,
        IReadOnlyList<IHardwareDevice> existingDevices,
        bool dryRun,
        List<IHardwareDevice> devices,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var descriptor in descriptors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsRejected(descriptor.BusId, descriptor.PciId))
                {
                    LogSkippedRejected(descriptor.BusId, GpuDeviceIds.FormatPciId(descriptor.PciId));
                    continue;
                }

                if (FindManaged(descriptor.BusId, descriptor.PciId, existingDevices) is { } managed)
                {
                    devices.Add(managed);
                    LogKeptExisting(managed.Id);
                    continue;
                }

                if (busFactory.TryOpenBus(descriptor.BusId) is not { } bus)
                {
                    continue;
                }

                var keepBus = false;
                try
                {
                    keepBus = ProbeBus(bus, existingDevices, dryRun, devices);
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
            }
        }
        catch (OperationCanceledException)
        {
            await DisposeNewDevicesAsync(devices, existingDevices).ConfigureAwait(false);
            throw;
        }

        return devices;
    }

    /// <summary>The already-managed device for this bus and PCI identity, derived from ids only (no I2C access).</summary>
    private IHardwareDevice? FindManaged(string busId, GpuPciIdentity pci, IReadOnlyList<IHardwareDevice> existingDevices)
    {
        if (existingDevices.Count == 0)
        {
            return null;
        }

        var candidates = GpuPciCatalog.FindMatches(pci, manifests);
        if (candidates.Count > 0)
        {
            foreach (var candidate in candidates)
            {
                var prefix = GpuDeviceIds.PrefixFor(candidate.Protocol);
                if (prefix is not null
                    && FindExisting(existingDevices, GpuDeviceIds.Create(prefix, busId, candidate.I2cAddress)) is { } existing)
                {
                    return existing;
                }
            }

            return null;
        }

        return IsUnlistedAsus(pci)
            ? FindExisting(existingDevices, GpuDeviceIds.Create(GpuDeviceIds.AsusPrefix, busId, AsusAuraGpuProtocol.DefaultSlaveAddress))
            : null;
    }

    private static bool IsUnlistedAsus(GpuPciIdentity pci) =>
        pci.VendorId == GpuPciIdentity.NvidiaVendorId && pci.SubVendorId == GpuPciIdentity.AsusSubVendorId;

    private static string RejectKey(string busId, GpuPciIdentity pci) => $"{busId}|{GpuDeviceIds.FormatPciId(pci)}";

    private bool IsRejected(string busId, GpuPciIdentity pci)
    {
        lock (_rejected)
        {
            return _rejected.Contains(RejectKey(busId, pci));
        }
    }

    private void Reject(string busId, GpuPciIdentity pci)
    {
        lock (_rejected)
        {
            _rejected.Add(RejectKey(busId, pci));
        }
    }

    /// <summary>Notebook GPUs share ASUS subsystem ids with unrelated embedded controllers; the fallback is desktop-only.</summary>
    private static bool IsMobileAdapter(string adapterName) =>
        adapterName.Contains("Laptop", StringComparison.OrdinalIgnoreCase)
        || adapterName.Contains("Mobile", StringComparison.OrdinalIgnoreCase);

    /// <summary>Disposes devices opened by an interrupted scan; devices that were already managed stay untouched.</summary>
    private static async ValueTask DisposeNewDevicesAsync(List<IHardwareDevice> devices, IReadOnlyList<IHardwareDevice> existingDevices)
    {
        foreach (var device in devices)
        {
            if (!ContainsInstance(existingDevices, device))
            {
                try
                {
                    await device.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Cleanup after cancellation is best effort; the cancellation itself must still surface.
                }
            }
        }
    }

    /// <summary>Returns true when ownership of <paramref name="bus"/> moved to a newly opened device.</summary>
    private bool ProbeBus(IGpuI2cBus bus, IReadOnlyList<IHardwareDevice> existingDevices, bool dryRun, List<IHardwareDevice> devices)
    {
        var pci = bus.PciId;
        if (IsRejected(bus.BusId, pci))
        {
            LogSkippedRejected(bus.BusId, GpuDeviceIds.FormatPciId(pci));
            return false;
        }

        if (FindManaged(bus.BusId, pci, existingDevices) is { } managed)
        {
            devices.Add(managed);
            LogKeptExisting(managed.Id);
            return false;
        }

        var candidates = GpuPciCatalog.FindMatches(pci, manifests);
        if (candidates.Count > 0)
        {
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

        if (IsUnlistedAsus(pci))
        {
            return ProbeUnlistedAsus(bus, dryRun, devices);
        }

        LogUnsupportedCard(GpuDeviceIds.FormatPciId(pci));
        Reject(bus.BusId, pci);
        return false;
    }

    /// <summary>
    /// Unlisted ASUS fallback: desktop GPUs only, a pure read-only ENE presence check, then the unified
    /// <c>AUMA</c>/<c>AURA</c> signature check inside <see cref="AsusAuraGpuDevice.TryOpen"/>. Cards that fail are
    /// remembered so later hotplug rescans never probe them again.
    /// </summary>
    private bool ProbeUnlistedAsus(IGpuI2cBus bus, bool dryRun, List<IHardwareDevice> devices)
    {
        var pci = bus.PciId;
        if (IsMobileAdapter(bus.AdapterName))
        {
            LogMobileSkipped(GpuDeviceIds.FormatPciId(pci), bus.AdapterName);
            Reject(bus.BusId, pci);
            return false;
        }

        var name = string.IsNullOrWhiteSpace(bus.AdapterName) ? DefaultAsusName : bus.AdapterName;
        var entry = new GpuCatalogEntry(
            pci,
            name,
            "ASUS",
            DeviceProtocolFamily.AsusAuraGpuI2c,
            AsusAuraGpuProtocol.DefaultSlaveAddress);
        if (AsusAuraGpuDevice.TryOpen(bus, entry, dryRun, requireReadOnlyPresence: true) is not { } device)
        {
            LogSignatureRejected(GpuDeviceIds.FormatPciId(pci));
            Reject(bus.BusId, pci);
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

    private static bool ContainsInstance(IReadOnlyList<IHardwareDevice> devices, IHardwareDevice device)
    {
        for (var i = 0; i < devices.Count; i++)
        {
            if (ReferenceEquals(devices[i], device))
            {
                return true;
            }
        }

        return false;
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Unlisted ASUS GPU {PciId} rejected: no ENE controller acknowledged a read or its name is not an Aura GPU signature (no mode/color writes; register-pointer select only)")]
    private partial void LogSignatureRejected(string pciId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Unlisted ASUS GPU {PciId} ({AdapterName}) skipped: the signature fallback is desktop-only (no I2C traffic)")]
    private partial void LogMobileSkipped(string pciId, string adapterName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "GPU bus {BusId} ({PciId}) was rejected earlier; not reopening it")]
    private partial void LogSkippedRejected(string busId, string pciId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "GPU RGB candidate {Name} at 0x{Address:X2} failed to open")]
    private partial void LogCandidateFailed(Exception exception, string name, byte address);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Probing GPU I2C bus {BusLocation} failed")]
    private partial void LogBusProbeFailed(Exception exception, string busLocation);
}
