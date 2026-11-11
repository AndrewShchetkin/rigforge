using System.Globalization;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.Gpu.Msi;

/// <summary>
/// MSI Mystic Light GPU (Blackwell / Lovelace) driven in volatile software mode. Frames are diffed against the
/// last acknowledged packet, so an unchanged frame performs no I2C traffic; nothing is saved to flash.
/// </summary>
internal sealed class MsiMysticGpuDevice : IRgbDevice
{
    private const string ZoneId = "gpu";

    private readonly IGpuI2cBus _bus;
    private readonly GpuCatalogEntry _entry;
    private readonly byte _slaveAddress;
    private readonly bool _dryRun;
    private readonly MsiGpuLayout _layout;
    private readonly Lock _lock = new();
    private readonly byte[] _packet = new byte[MsiMysticGpuProtocol.MaxFrameLength];
    private readonly byte[] _sentCache = new byte[MsiMysticGpuProtocol.MaxFrameLength];
    private int _ledCount;
    private int _frameLength;
    private bool _hasSentFrame;
    private readonly bool _initialized;
    private bool _disposed;
    private IReadOnlyList<RgbZone> _zones;
    private IReadOnlyDictionary<string, string> _properties;

    private MsiMysticGpuDevice(IGpuI2cBus bus, GpuCatalogEntry entry, byte slaveAddress, MsiGpuLayout layout, bool dryRun, bool initialized)
    {
        _bus = bus;
        _entry = entry;
        _slaveAddress = slaveAddress;
        _layout = layout;
        _dryRun = dryRun;
        _initialized = initialized;
        _ledCount = layout.DefaultLedCount;
        _frameLength = MsiMysticGpuProtocol.FrameLength(layout, _ledCount);
        _zones = BuildZones();
        _properties = BuildProperties();
        Id = GpuDeviceIds.Create(GpuDeviceIds.MsiPrefix, bus.BusId, slaveAddress);
    }

    public string Id { get; }

    public string Name => _entry.Name;

    public string Vendor => "MSI";

    public DeviceCapabilities Capabilities => DeviceCapabilities.Rgb;

    public int MaxFramesPerSecond => 30;

    public IReadOnlyDictionary<string, string> Properties => Volatile.Read(ref _properties);

    public IReadOnlyList<RgbZone> Zones => Volatile.Read(ref _zones);

    /// <summary>
    /// Switches the card into volatile software mode (unless <paramref name="dryRun"/>). Returns <c>null</c> when the
    /// controller does not acknowledge; the caller then keeps ownership of <paramref name="bus"/>.
    /// </summary>
    public static MsiMysticGpuDevice? TryOpen(IGpuI2cBus bus, GpuCatalogEntry entry, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(entry);
        var slaveAddress = entry.I2cAddress;
        GpuI2cAddressFirewall.ValidateSlaveAddress(slaveAddress);
        var layout = MsiMysticGpuProtocol.ParseVariant(entry.Variant);

        if (!dryRun)
        {
            using var busLock = bus.AcquireBusLock();
            if (!MsiMysticGpuProtocol.TryInitialize(bus, slaveAddress, layout.Generation))
            {
                return null;
            }
        }

        return new MsiMysticGpuDevice(bus, entry, slaveAddress, layout, dryRun, initialized: !dryRun);
    }

    public bool TryResizeZone(string zoneId, int ledCount)
    {
        if (!_layout.IsResizable
            || !string.Equals(zoneId, ZoneId, StringComparison.Ordinal)
            || ledCount < 1
            || ledCount > _layout.MaxLedCount)
        {
            return false;
        }

        lock (_lock)
        {
            _ledCount = ledCount;
            _frameLength = MsiMysticGpuProtocol.FrameLength(_layout, ledCount);
            _hasSentFrame = false;
            Volatile.Write(ref _zones, BuildZones());
            Volatile.Write(ref _properties, BuildProperties());
        }

        return true;
    }

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

            var length = MsiMysticGpuProtocol.BuildFrame(_layout, _ledCount, colors.Span, _frameLength, _packet);
            var frame = _packet.AsSpan(0, length);
            if (_hasSentFrame && frame.SequenceEqual(_sentCache.AsSpan(0, length)))
            {
                return ValueTask.CompletedTask;
            }

            using (_bus.AcquireBusLock())
            {
                if (!MsiMysticGpuProtocol.TryWriteFrame(_bus, _slaveAddress, _layout.Generation, frame))
                {
                    throw new IOException($"MSI GPU at 0x{_slaveAddress:X2} did not acknowledge a color frame.");
                }
            }

            frame.CopyTo(_sentCache);
            _hasSentFrame = true;
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
            if (!_dryRun && _initialized)
            {
                try
                {
                    // One volatile all-black frame; there is no flash save in this protocol.
                    var length = MsiMysticGpuProtocol.BuildFrame(_layout, _ledCount, [], _frameLength, _packet);
                    using var busLock = _bus.AcquireBusLock();
                    _ = MsiMysticGpuProtocol.TryWriteFrame(_bus, _slaveAddress, _layout.Generation, _packet.AsSpan(0, length));
                }
                catch (Exception)
                {
                    // Best effort during shutdown.
                }
            }

            _bus.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private RgbZone[] BuildZones() =>
    [
        new RgbZone
        {
            Id = ZoneId,
            Name = "GPU RGB",
            Topology = LedTopology.Linear,
            Leds = LedLayouts.Linear(_ledCount),
            MaxLedCount = _layout.MaxLedCount,
            IsResizable = _layout.IsResizable,
        },
    ];

    private Dictionary<string, string> BuildProperties() => new()
    {
        ["Bus"] = _bus.Location,
        ["PciId"] = GpuDeviceIds.FormatPciId(_bus.PciId),
        ["Address"] = GpuDeviceIds.FormatAddress(_slaveAddress),
        ["Generation"] = _layout.Generation.ToString(),
        ["Model"] = _layout.Model,
        ["LedCount"] = _ledCount.ToString(CultureInfo.InvariantCulture),
    };
}
