using System.Globalization;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Smbus;

namespace RigForge.Infrastructure.CrossPlatform.Dram;

/// <summary>ENE RGB DRAM sticks in direct mode. Frames are register-diffed and allocation-free.</summary>
internal sealed class EneDramDevice : IRgbDevice
{
    private readonly ISmbusController _bus;
    private readonly byte[] _addresses;
    private readonly byte?[,] _cache;
    private readonly EneRegisterWrite[] _frame;
    private readonly int _ledsPerStick;
    private readonly bool _dryRun;
    private readonly Lock _transaction = new();
    private readonly Dictionary<string, string> _properties;
    private bool _disposed;

    public EneDramDevice(
        ISmbusController bus,
        IReadOnlyList<EneSlotProbeResult> slots,
        IReadOnlyList<SmbiosMemoryStick> smbiosSticks,
        int ledsPerStick,
        bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count == 0)
        {
            throw new ArgumentException("At least one detected ENE DIMM is required.", nameof(slots));
        }

        if (ledsPerStick is < 1 or > EneDramProtocol.MaxLedsPerDimm)
        {
            throw new ArgumentOutOfRangeException(nameof(ledsPerStick));
        }

        _bus = bus;
        _addresses = slots.Select(static slot => slot.Address).ToArray();
        _ledsPerStick = ledsPerStick;
        _cache = new byte?[_addresses.Length, EneDramProtocol.CacheWidth(ledsPerStick)];
        _frame = new EneRegisterWrite[_addresses.Length * EneDramProtocol.CacheWidth(ledsPerStick)];
        _dryRun = dryRun;

        var slotKey = string.Join('-', slots.Select(static slot => slot.Address.ToString("x2", CultureInfo.InvariantCulture)));
        Id = $"ene-dram:{Sanitize(bus.BusName)}:{slotKey}";
        Zones = slots.Select(slot => new RgbZone
        {
            Id = $"dimm-{slot.Address:x2}",
            Name = $"DIMM 0x{slot.Address:X2}",
            Topology = LedTopology.Linear,
            Leds = LedLayouts.Linear(ledsPerStick),
            MaxLedCount = ledsPerStick,
            IsResizable = false,
        }).ToArray();

        _properties = new Dictionary<string, string>
        {
            ["Bus"] = bus.Location,
            ["Addresses"] = string.Join(", ", slots.Select(static slot => $"0x{slot.Address:X2}")),
            ["LedsPerDimm"] = ledsPerStick.ToString(CultureInfo.InvariantCulture),
        };
        if (slots.Any(static slot => !string.IsNullOrWhiteSpace(slot.Signature)))
        {
            _properties["Signature"] = string.Join(", ", slots.Select(static slot => slot.Signature).Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct());
        }

        CopyParts(smbiosSticks, _properties);
    }

    public string Id { get; }

    public string Name => "ENE RGB DRAM";

    public string Vendor => "ENE";

    public DeviceCapabilities Capabilities => DeviceCapabilities.Rgb;

    public IReadOnlyDictionary<string, string> Properties => _properties;

    public IReadOnlyList<RgbZone> Zones { get; }

    public int MaxFramesPerSecond => 20;

    public bool TryResizeZone(string zoneId, int ledCount) => false;

    public ValueTask UpdateLedsAsync(ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_dryRun)
        {
            return ValueTask.CompletedTask;
        }

        lock (_transaction)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            var written = EneDramProtocol.BuildFrameWrites(_addresses, colors.Span, _ledsPerStick, _cache, _frame);
            if (written == 0)
            {
                return ValueTask.CompletedTask;
            }

            using var busLock = _bus.AcquireBusLock();
            for (var i = 0; i < written; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var write = _frame[i];
                if (!EneDramProtocol.TryWriteRegister(_bus, write.Address, write.Register, write.Value))
                {
                    throw new IOException($"ENE DRAM at 0x{write.Address:X2} did not acknowledge register 0x{write.Register:X4}.");
                }

                _cache[write.Slot, write.CacheIndex] = write.Value;
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        lock (_transaction)
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

    internal static void CopyParts(IReadOnlyList<SmbiosMemoryStick> sticks, Dictionary<string, string> properties)
    {
        if (sticks.Count == 0)
        {
            return;
        }

        var parts = sticks
            .Select(static stick => stick.PartNumber.Trim())
            .Where(static part => part.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (parts.Length > 0)
        {
            properties["PartNumber"] = string.Join(", ", parts);
        }
    }

    private static string Sanitize(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        foreach (var ch in value)
        {
            buffer[length++] = char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-';
        }

        return new string(buffer[..length]).Trim('-');
    }
}
