using System.Globalization;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Smbus;

namespace RigForge.Infrastructure.CrossPlatform.Kingston;

/// <summary>
/// Kingston FURY Beast / Renegade DDR5 DRAM RGB controller driven over SMBus in Direct Mode.
/// Each populated RGB DIMM slot (<c>0x60..0x67</c>) is exposed as a 12-LED linear zone on the spatial canvas.
/// </summary>
internal sealed class KingstonFuryDramDevice : IRgbDevice
{
    private readonly ISmbusController _bus;
    private readonly byte[] _slotAddresses;
    private readonly byte?[,] _registerCache;
    private readonly byte _brightness;
    private readonly byte _maxAllowedAddress;
    private readonly bool _dryRun;
    private readonly Action<TimeSpan> _delay;
    private readonly Lock _transaction = new();
    private readonly Dictionary<string, string> _properties;
    private bool _preambleSent;
    private bool _disposed;

    public KingstonFuryDramDevice(
        ISmbusController bus,
        IReadOnlyList<KingstonSlotProbeResult> slots,
        IReadOnlyList<SmbiosMemoryStick> smbiosSticks,
        int maxFramesPerSecond,
        int brightnessPercent,
        byte maxAllowedAddress,
        bool dryRun,
        Action<TimeSpan>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count == 0)
        {
            throw new ArgumentException("At least one detected Kingston FURY slot is required.", nameof(slots));
        }

        _bus = bus;
        _slotAddresses = slots.Select(static s => s.Address).ToArray();
        _registerCache = new byte?[_slotAddresses.Length, 256];
        _brightness = (byte)Math.Clamp(brightnessPercent, 0, KingstonFuryDramProtocol.MaxBrightness);
        _maxAllowedAddress = maxAllowedAddress;
        _dryRun = dryRun;
        _delay = delay ?? Thread.Sleep;

        var slotKey = string.Join('-', slots.Select(static s => s.SlotIndex.ToString(CultureInfo.InvariantCulture)));
        Id = $"kingston-fury-ddr5:{SanitizeId(bus.BusName)}:{slotKey}";
        MaxFramesPerSecond = Math.Clamp(maxFramesPerSecond, 1, 60);

        Zones = slots
            .Select(static slot => new RgbZone
            {
                Id = $"dimm-{slot.SlotIndex + 1}",
                Name = $"DIMM {slot.SlotIndex + 1} (0x{slot.Address:X2})",
                Topology = LedTopology.Linear,
                Leds = LedLayouts.Linear(KingstonFuryDramProtocol.LedsPerDimmDdr5),
                MaxLedCount = KingstonFuryDramProtocol.LedsPerDimmDdr5,
                IsResizable = false,
            })
            .ToArray();

        _properties = new Dictionary<string, string>
        {
            ["Bus"] = bus.Location,
            ["Addresses"] = string.Join(", ", slots.Select(static s => $"0x{s.Address:X2}")),
            ["Models"] = string.Join(", ", slots.Select(static s => s.ModelCode is { } code ? KingstonFuryDramProtocol.DescribeModel(code) : "unknown").Distinct()),
        };

        if (smbiosSticks.Count > 0)
        {
            var parts = smbiosSticks
                .Select(static s => s.PartNumber.Trim())
                .Where(static p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (parts.Length > 0)
            {
                _properties["PartNumber"] = string.Join(", ", parts);
            }
        }

        if (!_dryRun)
        {
            SendPreambleLocked();
        }
    }

    public string Id { get; }

    public string Name => "Kingston FURY DDR5 RGB";

    public string Vendor => "Kingston";

    public DeviceCapabilities Capabilities => DeviceCapabilities.Rgb;

    public IReadOnlyDictionary<string, string> Properties => _properties;

    public IReadOnlyList<RgbZone> Zones { get; }

    public int MaxFramesPerSecond { get; }

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

            var plan = KingstonFuryDramProtocol.BuildFrameWritePlan(_slotAddresses, colors.Span, _brightness, _registerCache);
            if (plan.Count == 0)
            {
                return ValueTask.CompletedTask;
            }

            if (!_preambleSent)
            {
                SendPreambleLocked();
            }

            SendBeginTransactionLocked();
            try
            {
                foreach (var batch in plan)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var (slotPos, address, value) in batch.Writes)
                    {
                        if (WriteRegisterWithRetryLocked(address, batch.Register, value))
                        {
                            _registerCache[slotPos, batch.Register] = value;
                        }
                        else
                        {
                            throw new IOException($"Kingston FURY DIMM at 0x{address:X2} did not acknowledge register 0x{batch.Register:X2}.");
                        }
                    }
                }
            }
            finally
            {
                SendEndTransactionLocked();
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

    private void SendPreambleLocked()
    {
        SendBeginTransactionLocked();
        try
        {
            foreach (var address in _slotAddresses)
            {
                // In Direct Mode each stick uses independent index 0 (matches OpenRGB's SendPreamble(false)).
                if (!WriteRegisterWithRetryLocked(address, KingstonFuryDramProtocol.RegisterIndex, 0x00))
                {
                    throw new IOException($"Failed to write direct-mode index to Kingston FURY DIMM at 0x{address:X2}.");
                }
            }

            _delay(KingstonFuryDramProtocol.TransactionDelay);
            _preambleSent = true;
        }
        finally
        {
            SendEndTransactionLocked();
        }
    }

    private void SendBeginTransactionLocked()
    {
        foreach (var address in _slotAddresses)
        {
            if (!WriteRegisterWithRetryLocked(address, KingstonFuryDramProtocol.RegisterApply, KingstonFuryDramProtocol.TransactionBegin))
            {
                throw new IOException($"Failed to begin SMBus transaction on Kingston FURY DIMM at 0x{address:X2}.");
            }
        }

        _delay(KingstonFuryDramProtocol.TransactionDelay);
    }

    private void SendEndTransactionLocked()
    {
        foreach (var address in _slotAddresses)
        {
            _ = WriteRegisterWithRetryLocked(address, KingstonFuryDramProtocol.RegisterApply, KingstonFuryDramProtocol.TransactionEnd);
        }

        _delay(KingstonFuryDramProtocol.TransactionDelay);
    }

    private bool WriteRegisterWithRetryLocked(byte address, byte register, byte value)
    {
        SmbusSafetyGuard.ValidateWrite(address, register, value, _maxAllowedAddress);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            if (_bus.TryWriteByteData(address, register, value))
            {
                return true;
            }

            if (attempt < 5)
            {
                _delay(KingstonFuryDramProtocol.RetryDelay);
            }
        }

        return false;
    }

    private static string SanitizeId(string value)
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
