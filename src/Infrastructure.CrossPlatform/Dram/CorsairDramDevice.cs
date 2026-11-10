using System.Globalization;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Smbus;

namespace RigForge.Infrastructure.CrossPlatform.Dram;

/// <summary>Corsair Vengeance / Dominator RGB DRAM. Direct frames are diff-cached and do not touch SPD.</summary>
internal sealed class CorsairDramDevice : IRgbDevice
{
    private readonly ISmbusController _bus;
    private readonly byte[] _addresses;
    private readonly byte?[,] _cache;
    private readonly CorsairRegisterWrite[] _frame;
    private readonly int _ledsPerStick;
    private readonly bool _dryRun;
    private readonly Lock _transaction = new();
    private readonly Dictionary<string, string> _properties;
    private bool _disposed;

    public CorsairDramDevice(
        ISmbusController bus,
        IReadOnlyList<CorsairSlotProbeResult> slots,
        IReadOnlyList<SmbiosMemoryStick> smbiosSticks,
        int ledsPerStick,
        bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count == 0)
        {
            throw new ArgumentException("At least one detected Corsair DIMM is required.", nameof(slots));
        }

        _bus = bus;
        _addresses = slots.Select(static slot => slot.Address).ToArray();
        _ledsPerStick = ledsPerStick;
        _cache = new byte?[_addresses.Length, 256];
        _frame = new CorsairRegisterWrite[_addresses.Length * (1 + (ledsPerStick * 3) + 1)];
        _dryRun = dryRun;

        var slotKey = string.Join('-', slots.Select(static slot => slot.SlotIndex.ToString(CultureInfo.InvariantCulture)));
        Id = $"corsair-dram:{Sanitize(bus.BusName)}:{slotKey}";
        Zones = slots.Select(slot => new RgbZone
        {
            Id = $"dimm-{slot.SlotIndex + 1}",
            Name = $"DIMM {slot.SlotIndex + 1} (0x{slot.Address:X2})",
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
        EneDramDevice.CopyParts(smbiosSticks, _properties);
    }

    public string Id { get; }

    public string Name => "Corsair RGB DRAM";

    public string Vendor => "Corsair";

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

            var written = CorsairDramProtocol.BuildFrameWrites(_addresses, colors.Span, _ledsPerStick, _cache, _frame);
            if (written == 0)
            {
                return ValueTask.CompletedTask;
            }

            using var busLock = _bus.AcquireBusLock();
            for (var i = 0; i < written; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var write = _frame[i];
                if (!CorsairDramProtocol.TryWriteRegister(_bus, write.Address, write.Register, write.Value))
                {
                    throw new IOException($"Corsair DRAM at 0x{write.Address:X2} did not acknowledge register 0x{write.Register:X2}.");
                }

                if (write.Remember)
                {
                    _cache[write.Slot, write.Register] = write.Value;
                }
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
