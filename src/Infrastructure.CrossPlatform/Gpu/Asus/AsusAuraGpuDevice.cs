using System.Globalization;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.Gpu.Asus;

/// <summary>
/// ASUS Aura GPU (ENE controller) driven in volatile direct mode. Frames are register-diffed: an unchanged frame
/// performs no I2C traffic, and nothing is ever written to the controller's flash.
/// </summary>
internal sealed class AsusAuraGpuDevice : IRgbDevice
{
    private const string ZoneId = "gpu";

    private readonly IGpuI2cBus _bus;
    private readonly byte _slaveAddress;
    private readonly bool _dryRun;
    private readonly bool _directModeEnabled;
    private readonly string _controllerName;
    private readonly GpuCatalogEntry _entry;
    private readonly Lock _lock = new();
    private readonly byte[] _frameBytes = new byte[AsusAuraGpuProtocol.MaxLeds * 3];
    private readonly byte[] _sentCache = new byte[AsusAuraGpuProtocol.MaxLeds * 3];
    private readonly byte[] _chunkPacket = new byte[AsusAuraGpuProtocol.MaxChunkPayload + 1];
    private int _ledCount;
    private bool _hasSentFrame;
    private bool _disposed;
    private IReadOnlyList<RgbZone> _zones;
    private IReadOnlyDictionary<string, string> _properties;

    private AsusAuraGpuDevice(
        IGpuI2cBus bus,
        GpuCatalogEntry entry,
        byte slaveAddress,
        string controllerName,
        int ledCount,
        bool dryRun,
        bool directModeEnabled)
    {
        _bus = bus;
        _entry = entry;
        _slaveAddress = slaveAddress;
        _controllerName = controllerName;
        _ledCount = ledCount;
        _dryRun = dryRun;
        _directModeEnabled = directModeEnabled;
        _zones = BuildZones(ledCount);
        _properties = BuildProperties();
        Id = GpuDeviceIds.Create(GpuDeviceIds.AsusPrefix, bus.BusId, slaveAddress);
    }

    public string Id { get; }

    public string Name => _entry.Name;

    public string Vendor => "ASUS";

    public DeviceCapabilities Capabilities => DeviceCapabilities.Rgb;

    public int MaxFramesPerSecond => 30;

    public IReadOnlyDictionary<string, string> Properties => Volatile.Read(ref _properties);

    public IReadOnlyList<RgbZone> Zones => Volatile.Read(ref _zones);

    /// <summary>
    /// Under one bus lock: (optionally) a pure read-only ENE presence check, the controller-name read, the mandatory
    /// <c>AUMA</c>/<c>AURA</c> signature check (catalog-listed and unlisted cards alike), the LED count read and,
    /// unless <paramref name="dryRun"/>, enabling volatile direct mode. Returns <c>null</c> when the controller does
    /// not answer or is not an Aura GPU; no mode/color write has been sent in that case (register-pointer selects
    /// only) and the caller keeps ownership of <paramref name="bus"/>.
    /// </summary>
    public static AsusAuraGpuDevice? TryOpen(IGpuI2cBus bus, GpuCatalogEntry entry, bool dryRun, bool requireReadOnlyPresence = false)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(entry);
        var slaveAddress = entry.I2cAddress;
        GpuI2cAddressFirewall.ValidateSlaveAddress(slaveAddress);

        using var busLock = bus.AcquireBusLock();
        if (requireReadOnlyPresence && !AsusAuraGpuProtocol.TryProbePresenceReadOnly(bus, slaveAddress))
        {
            return null;
        }

        if (!AsusAuraGpuProtocol.TryReadControllerName(bus, slaveAddress, out var controllerName)
            || !AsusAuraGpuProtocol.IsAuraGpuSignature(controllerName))
        {
            return null;
        }

        var ledCount = AsusAuraGpuProtocol.ReadLedCount(bus, slaveAddress, out _);
        if (!dryRun && !AsusAuraGpuProtocol.TryEnableDirectMode(bus, slaveAddress))
        {
            _ = AsusAuraGpuProtocol.TryWriteRegister(bus, slaveAddress, AsusAuraGpuProtocol.DirectAccess, 0x00);
            return null;
        }

        return new AsusAuraGpuDevice(bus, entry, slaveAddress, controllerName, ledCount, dryRun, directModeEnabled: !dryRun);
    }

    public bool TryResizeZone(string zoneId, int ledCount)
    {
        if (!string.Equals(zoneId, ZoneId, StringComparison.Ordinal) || ledCount is < 1 or > AsusAuraGpuProtocol.MaxLeds)
        {
            return false;
        }

        lock (_lock)
        {
            _ledCount = ledCount;
            _hasSentFrame = false;
            Volatile.Write(ref _zones, BuildZones(ledCount));
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

            var totalBytes = AsusAuraGpuProtocol.PackFrame(colors.Span, _ledCount, _frameBytes);
            var frame = _frameBytes.AsSpan(0, totalBytes);
            if (_hasSentFrame && frame.SequenceEqual(_sentCache.AsSpan(0, totalBytes)))
            {
                return ValueTask.CompletedTask;
            }

            using (_bus.AcquireBusLock())
            {
                if (!AsusAuraGpuProtocol.TryWriteFrame(_bus, _slaveAddress, frame, _chunkPacket))
                {
                    throw new IOException($"ASUS Aura GPU at 0x{_slaveAddress:X2} did not acknowledge a color frame.");
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
            if (!_dryRun && _directModeEnabled)
            {
                try
                {
                    using var busLock = _bus.AcquireBusLock();
                    AsusAuraGpuProtocol.RestoreHardwareRainbow(_bus, _slaveAddress);
                }
                catch (Exception)
                {
                    // Best effort during shutdown: the card keeps its last volatile state at worst.
                }
            }

            _bus.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private static RgbZone[] BuildZones(int ledCount) =>
    [
        new RgbZone
        {
            Id = ZoneId,
            Name = "GPU RGB",
            Topology = LedTopology.Linear,
            Leds = AsusAuraGpuProtocol.CreateReversedLayout(ledCount),
            MaxLedCount = AsusAuraGpuProtocol.MaxLeds,
            IsResizable = true,
        },
    ];

    private Dictionary<string, string> BuildProperties() => new()
    {
        ["Bus"] = _bus.Location,
        ["PciId"] = GpuDeviceIds.FormatPciId(_bus.PciId),
        ["Address"] = GpuDeviceIds.FormatAddress(_slaveAddress),
        ["Controller"] = _controllerName,
        ["LedCount"] = _ledCount.ToString(CultureInfo.InvariantCulture),
    };
}
