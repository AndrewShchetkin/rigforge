using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.Gpu.Gigabyte;

/// <summary>
/// Gigabyte RGB Fusion GPU (Gen4 <c>0x75</c> / Gen2 <c>0x71</c>). Every lighting zone is a single point LED; frames are
/// diffed per zone so unchanged zones send no packet. All writes are volatile (no flash save).
/// </summary>
internal sealed class GigabyteFusionGpuDevice : IRgbDevice
{
    private readonly IGpuI2cBus _bus;
    private readonly GpuCatalogEntry _entry;
    private readonly IPrecisionTimer _timer;
    private readonly byte _slaveAddress;
    private readonly bool _dryRun;
    private readonly GigabyteGpuLayout _layout;
    private readonly RgbColor?[] _zoneCache;
    private readonly Lock _lock = new();
    private bool _disposed;

    private GigabyteFusionGpuDevice(IGpuI2cBus bus, GpuCatalogEntry entry, byte slaveAddress, GigabyteGpuLayout layout, bool dryRun, IPrecisionTimer timer)
    {
        _bus = bus;
        _entry = entry;
        _slaveAddress = slaveAddress;
        _layout = layout;
        _dryRun = dryRun;
        _timer = timer;
        _zoneCache = new RgbColor?[layout.Zones.Length];
        Id = GpuDeviceIds.Create(GpuDeviceIds.GigabytePrefix, bus.BusId, slaveAddress);

        Zones = [.. layout.Zones.Select(static zone => new RgbZone
        {
            Id = zone.Id,
            Name = zone.Name,
            Topology = LedTopology.Point,
            Leds = LedLayouts.Point(),
            MaxLedCount = 1,
            IsResizable = false,
        })];

        Properties = new Dictionary<string, string>
        {
            ["Bus"] = bus.Location,
            ["PciId"] = GpuDeviceIds.FormatPciId(bus.PciId),
            ["Address"] = GpuDeviceIds.FormatAddress(slaveAddress),
            ["Generation"] = layout.Generation.ToString(),
            ["Model"] = layout.Model,
        };
    }

    public string Id { get; }

    public string Name => _entry.Name;

    public string Vendor => "Gigabyte";

    public DeviceCapabilities Capabilities => DeviceCapabilities.Rgb;

    public int MaxFramesPerSecond => 30;

    public IReadOnlyDictionary<string, string> Properties { get; }

    public IReadOnlyList<RgbZone> Zones { get; }

    /// <summary>
    /// Verifies / initializes the controller (unless <paramref name="dryRun"/>). Returns <c>null</c> when the controller
    /// does not acknowledge; the caller then keeps ownership of <paramref name="bus"/> and may try the next candidate.
    /// </summary>
    public static GigabyteFusionGpuDevice? TryOpen(IGpuI2cBus bus, GpuCatalogEntry entry, bool dryRun, IPrecisionTimer timer)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(timer);
        var slaveAddress = entry.I2cAddress;
        GpuI2cAddressFirewall.ValidateSlaveAddress(slaveAddress);
        var layout = GigabyteFusionGpuProtocol.ParseVariant(entry.Variant);

        using (bus.AcquireBusLock())
        {
            if (layout.Generation == GigabyteGpuGeneration.Gen2)
            {
                var present = dryRun
                    ? GigabyteFusionGpuProtocol.TryProbeGen2ReadOnly(bus, slaveAddress)
                    : GigabyteFusionGpuProtocol.TryProbeGen2(bus, slaveAddress)
                        && GigabyteFusionGpuProtocol.TryInitializeGen2(bus, slaveAddress);
                if (!present)
                {
                    return null;
                }
            }
            else if (!dryRun && !GigabyteFusionGpuProtocol.TryInitializeGen4(bus, slaveAddress, layout, timer))
            {
                return null;
            }
        }

        return new GigabyteFusionGpuDevice(bus, entry, slaveAddress, layout, dryRun, timer);
    }

    public bool TryResizeZone(string zoneId, int ledCount) => false;

    public ValueTask UpdateLedsAsync(ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_dryRun)
        {
            return ValueTask.CompletedTask;
        }

        lock (_lock)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            var span = colors.Span;
            var zones = _layout.Zones;
            IDisposable? busLock = null;
            try
            {
                var wroteAny = false;
                for (var i = 0; i < zones.Length; i++)
                {
                    var color = i < span.Length ? span[i] : default;
                    if (_zoneCache[i] == color)
                    {
                        continue;
                    }

                    busLock ??= _bus.AcquireBusLock();
                    if (wroteAny)
                    {
                        _timer.Sleep(GigabyteFusionGpuProtocol.InterWriteDelay);
                    }

                    var written = _layout.Generation == GigabyteGpuGeneration.Gen2
                        ? GigabyteFusionGpuProtocol.TryWriteGen2Zone(_bus, _slaveAddress, zones[i].HardwareZone, color)
                        : GigabyteFusionGpuProtocol.TryWriteGen4Zone(_bus, _slaveAddress, _layout, zones[i].HardwareZone, color);
                    if (!written)
                    {
                        throw new IOException($"Gigabyte GPU at 0x{_slaveAddress:X2} did not acknowledge zone {zones[i].Name}.");
                    }

                    _zoneCache[i] = color;
                    wroteAny = true;
                }
            }
            finally
            {
                busLock?.Dispose();
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            _bus.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
