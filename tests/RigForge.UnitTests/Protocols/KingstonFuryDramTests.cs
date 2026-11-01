using System.Buffers.Binary;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform;
using RigForge.Infrastructure.CrossPlatform.Kingston;
using RigForge.Infrastructure.CrossPlatform.Smbus;
using RigForge.Infrastructure.Linux.Hardware;

namespace RigForge.UnitTests.Protocols;

public sealed class KingstonFuryDramTests
{
    [Theory]
    [InlineData(0x00)] // SMBus general call
    [InlineData(0x08)] // SMBus host notify
    [InlineData(0x0F)] // SMBus reserved ceiling
    [InlineData(0x36)] // DDR4 SPD page 0
    [InlineData(0x37)] // DDR4 SPD page 1
    [InlineData(0x48)] // DDR5 PMIC slot 0
    [InlineData(0x4C)] // DDR5 PMIC slot 4
    [InlineData(0x4F)] // DDR5 PMIC slot 7
    [InlineData(0x50)] // DDR5 SPD5118 slot 0
    [InlineData(0x53)] // DDR5 SPD5118 slot 3
    [InlineData(0x57)] // DDR5 SPD5118 slot 7
    [InlineData(0x78)] // 10-bit / reserved SMBus address
    [InlineData(0x7F)] // Upper reserved SMBus address
    public void Address_firewall_blocks_reserved_spd_and_pmic_ranges(byte forbiddenAddress)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SmbusAddressFirewall.ValidateAddress(forbiddenAddress));
    }

    [Theory]
    [InlineData(0x60)] // Kingston FURY DDR5 slot 0
    [InlineData(0x63)] // Kingston FURY DDR5 slot 3
    [InlineData(0x67)] // Kingston FURY DDR5 slot 7
    [InlineData(0x70)] // Non-SPD/PMIC peripheral address
    public void Address_firewall_allows_rgb_and_peripheral_addresses(byte allowedAddress)
    {
        var ex = Record.Exception(() => SmbusAddressFirewall.ValidateAddress(allowedAddress));
        Assert.Null(ex);
    }

    [Fact]
    public void Smbios_type17_parser_extracts_populated_memory_sticks_and_skips_empty_slots()
    {
        var dmi = BuildSmbiosType17Table(
            (SizeMb: 32768, SpeedMt: 6000, DeviceLocator: "DIMM A2", BankLocator: "P0 CHANNEL A", Manufacturer: "Kingston", Serial: "AA11BB22", PartNumber: "KF560C36-32  "),
            (SizeMb: 0, SpeedMt: 0, DeviceLocator: "DIMM B1", BankLocator: "P0 CHANNEL B", Manufacturer: "", Serial: "", PartNumber: ""));

        var sticks = SmbiosType17Parser.Parse(dmi);

        var stick = Assert.Single(sticks);
        Assert.Equal("P0 CHANNEL A", stick.BankLocator);
        Assert.Equal("DIMM A2", stick.DeviceLocator);
        Assert.Equal("Kingston", stick.Manufacturer);
        Assert.Equal("KF560C36-32", stick.PartNumber);
        Assert.Equal("AA11BB22", stick.SerialNumber);
        Assert.Equal(32768, stick.CapacityMegabytes);
        Assert.Equal(6000, stick.SpeedMegaTransfers);
        Assert.True(stick.IsKingstonFuryDdr5);
    }

    [Theory]
    [InlineData("amdgpu", "edge", SensorRole.GpuTemperature)]
    [InlineData("radeon", "", SensorRole.GpuTemperature)]
    [InlineData("xe", "gpu", SensorRole.GpuTemperature)]
    [InlineData("i915", "pkg", SensorRole.GpuTemperature)]
    [InlineData("k10temp", "Tctl", SensorRole.CpuTemperature)]
    [InlineData("coretemp", "Package id 0", SensorRole.CpuTemperature)]
    [InlineData("amdgpu", "mem", SensorRole.None)]
    public void Linux_hwmon_maps_amd_and_intel_gpu_temperature_roles(string chip, string label, SensorRole expectedRole)
    {
        Assert.Equal(expectedRole, SysfsHwmonChip.RoleFor(chip, label));
    }
    [Theory]
    [InlineData(0x50)] // DDR5 SPD5118 slot 0
    [InlineData(0x51)] // DDR5 SPD5118 slot 1
    [InlineData(0x57)] // DDR5 SPD5118 slot 7
    [InlineData(0x48)] // DDR5 PMIC slot 0
    [InlineData(0x4F)] // DDR5 PMIC slot 7
    [InlineData(0x36)] // DDR4 SPD page 0
    [InlineData(0x37)] // DDR4 SPD page 1
    [InlineData(0x58)] // DDR4 Kingston Fury base
    [InlineData(0x64)] // Above default 4-slot range (0x60..0x63)
    [InlineData(0x68)] // Above hard 8-slot ceiling
    public void Safety_guard_blocks_all_non_rgb_addresses_including_spd_and_pmic(byte forbiddenAddress)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SmbusSafetyGuard.ValidateRead(forbiddenAddress, KingstonFuryDramProtocol.RegisterModel));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SmbusSafetyGuard.ValidateWrite(
                forbiddenAddress,
                KingstonFuryDramProtocol.RegisterApply,
                KingstonFuryDramProtocol.TransactionBegin));
    }

    [Theory]
    [InlineData(0x00, 0x00)] // Unknown register
    [InlineData(0x01, 0x46)] // Signature register is read-only
    [InlineData(0x06, 0x10)] // Model register is read-only
    [InlineData(0x08, 0x00)] // Apply register only accepts 0x53 or 0x44
    [InlineData(0x09, 0x00)] // Mode register only accepts Direct Mode (0x10)
    [InlineData(0x0B, 0x04)] // Index > 3
    [InlineData(0x20, 101)]  // Brightness > 100
    [InlineData(0x27, 0)]    // NumSlots < 1
    [InlineData(0x27, 5)]    // NumSlots > 4
    [InlineData(0x4F, 0xFF)] // Below LED base 0x50
    [InlineData(0x74, 0xFF)] // Above 12th LED blue register 0x73
    public void Safety_guard_blocks_forbidden_registers_and_values(byte register, byte value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SmbusSafetyGuard.ValidateWrite(0x61, register, value));
    }

    [Theory]
    [InlineData(0x10)] // Beast DDR5
    [InlineData(0x11)] // Renegade DDR5
    [InlineData(0x12)] // Beast White DDR5
    [InlineData(0x15)] // Beast2 DDR5 (KF556/KF560)
    public void Probe_slot_accepts_verified_fury_signature_and_supported_ddr5_models(byte modelCode)
    {
        var bus = new FakeSmbusController();
        bus.SeedFuryDimm(0x61, "FURY", modelCode);

        var result = KingstonFuryDramProtocol.ProbeSlot(bus, slotIndex: 1, delay: static _ => { });

        Assert.Equal(KingstonSlotProbeStatus.Supported, result.Status);
        Assert.Equal((byte)0x61, result.Address);
        Assert.Equal("FURY", result.Signature);
        Assert.Equal(modelCode, result.ModelCode);
        Assert.Equal(((byte)0x61, KingstonFuryDramProtocol.RegisterApply, KingstonFuryDramProtocol.TransactionBegin), bus.Writes[0]);
        Assert.Equal(((byte)0x61, KingstonFuryDramProtocol.RegisterApply, KingstonFuryDramProtocol.TransactionEnd), bus.Writes[^1]);
    }

    [Fact]
    public void Probe_slot_rejects_unknown_model_and_always_closes_transaction()
    {
        var bus = new FakeSmbusController();
        bus.SeedFuryDimm(0x61, "FURY", modelCode: 0x20); // Unsupported v2 protocol

        var result = KingstonFuryDramProtocol.ProbeSlot(bus, slotIndex: 1, delay: static _ => { });

        Assert.Equal(KingstonSlotProbeStatus.UnsupportedModel, result.Status);
        Assert.Equal(2, bus.Writes.Count);
        Assert.Equal(((byte)0x61, KingstonFuryDramProtocol.RegisterApply, KingstonFuryDramProtocol.TransactionBegin), bus.Writes[0]);
        Assert.Equal(((byte)0x61, KingstonFuryDramProtocol.RegisterApply, KingstonFuryDramProtocol.TransactionEnd), bus.Writes[1]);
    }

    [Fact]
    public async Task Identical_frame_produces_zero_smbus_writes_and_partial_change_writes_only_diff()
    {
        var bus = new FakeSmbusController();
        bus.SeedFuryDimm(0x61, "FURY", 0x10);
        bus.SeedFuryDimm(0x63, "FURY", 0x10);

        var slots = new[]
        {
            new KingstonSlotProbeResult(1, 0x61, KingstonSlotProbeStatus.Supported, "FURY", 0x10, "Beast"),
            new KingstonSlotProbeResult(3, 0x63, KingstonSlotProbeStatus.Supported, "FURY", 0x10, "Beast"),
        };

        var device = new KingstonFuryDramDevice(
            bus,
            slots,
            smbiosSticks: [],
            maxFramesPerSecond: 5,
            brightnessPercent: 80,
            maxAllowedAddress: 0x63,
            dryRun: false,
            delay: static _ => { });

        // Preamble happened on construction: Begin + Index + End across 2 sticks = 6 writes
        Assert.Equal(6, bus.Writes.Count);
        Assert.Equal(1, bus.BusLockAcquisitions);
        bus.Writes.Clear();
        bus.BusLockAcquisitions = 0;

        var frame = Enumerable.Range(0, 24).Select(i => new RgbColor((byte)i, (byte)(i + 10), (byte)(i + 20))).ToArray();
        await device.UpdateLedsAsync(frame, CancellationToken.None);

        // First frame: Begin(2) + Mode(2) + 36 LED regs * 2 sticks (72) + Brightness(2) + NumSlots(2) + End(2) = 82 writes
        Assert.Equal(82, bus.Writes.Count);
        Assert.Equal(1, bus.BusLockAcquisitions);
        bus.Writes.Clear();
        bus.BusLockAcquisitions = 0;

        // Second identical frame: 0 SMBus writes (not even Begin/End!)
        await device.UpdateLedsAsync(frame, CancellationToken.None);
        Assert.Empty(bus.Writes);
        Assert.Equal(0, bus.BusLockAcquisitions);

        // Change only the Green channel of LED 0 on stick 2 (index 12)
        frame[12] = new RgbColor(frame[12].R, 250, frame[12].B);
        await device.UpdateLedsAsync(frame, CancellationToken.None);

        // Begin(2) + 1 changed byte on 0x63 (0x51 = 250) + End(2) = 5 writes total
        Assert.Equal(5, bus.Writes.Count);
        Assert.Equal(1, bus.BusLockAcquisitions);
        Assert.Contains(((byte)0x63, KingstonFuryDramProtocol.RegisterBaseGreen, (byte)250), bus.Writes);
        Assert.DoesNotContain(bus.Writes, w => w.Address == 0x61 && w.Register == KingstonFuryDramProtocol.RegisterBaseGreen);
    }

    [Fact]
    public async Task Probe_does_not_open_smbus_when_smbios_has_no_kingston_ddr5()
    {
        var factory = new FakeSmbusFactory(
            [new SmbiosMemoryStick("BANK 0", "DIMM A2", "Corsair", "CMK32GX5M2B5600C36", "1234", 16384, 5600)],
            new FakeSmbusController());
        var options = Options.Create(new HardwareOptions());
        var probe = new KingstonFuryProbe(factory, options, NullLogger<KingstonFuryProbe>.Instance, delay: static _ => { });

        var devices = await probe.ProbeAsync(CancellationToken.None);

        Assert.Empty(devices);
        Assert.Equal(0, factory.OpenControllersCallCount);
    }

    [Fact]
    public async Task Probe_in_dry_run_detects_dimm_zones_without_writing_mode_or_colors()
    {
        var bus = new FakeSmbusController();
        bus.SeedFuryDimm(0x61, "FURY", 0x15);
        bus.SeedFuryDimm(0x63, "FURY", 0x15);

        var factory = new FakeSmbusFactory(
            [
                new SmbiosMemoryStick("P0 CHANNEL A", "DIMM 1", "Kingston", "KF556C40-32", "A1", 32768, 5600),
                new SmbiosMemoryStick("P0 CHANNEL B", "DIMM 1", "Kingston", "KF556C40-32", "A2", 32768, 5600),
            ],
            bus);
        var options = Options.Create(new HardwareOptions { DryRun = true });
        var probe = new KingstonFuryProbe(factory, options, NullLogger<KingstonFuryProbe>.Instance, delay: static _ => { });

        var devices = await probe.ProbeAsync(CancellationToken.None);

        var rgb = Assert.Single(devices) as KingstonFuryDramDevice;
        Assert.NotNull(rgb);
        Assert.Equal(2, rgb.Zones.Count);
        Assert.All(rgb.Zones, z => Assert.Equal(12, z.LedCount));
        // Dry-run only wrote RegisterApply (0x08) during signature probe; never RegisterIndex (0x0B), RegisterMode (0x09), or LEDs (0x50..0x73)
        Assert.All(bus.Writes, w => Assert.Equal(KingstonFuryDramProtocol.RegisterApply, w.Register));
    }

    private sealed class FakeSmbusFactory(IReadOnlyList<SmbiosMemoryStick> sticks, ISmbusController controller) : ISmbusControllerFactory
    {
        public string BackendName => "Fake";
        public bool IsAvailable => true;
        public string DiagnosticStatus => "Ready";
        public int OpenControllersCallCount { get; private set; }

        public IReadOnlyList<SmbiosMemoryStick> GetInstalledMemory() => sticks;

        public IReadOnlyList<ISmbusController> OpenControllers()
        {
            OpenControllersCallCount++;
            return [controller];
        }
    }

    private sealed class FakeSmbusController : ISmbusController
    {
        private readonly HashSet<byte> _presentAddresses = [];
        private readonly Dictionary<(byte Address, byte Register), ushort> _words = [];

        public string BusName => "piix4_0";
        public string Location => "Fake PIIX4 port 0";
        public int BusLockAcquisitions { get; set; }
        public List<(byte Address, byte Register, byte Value)> Writes { get; } = [];

        public IDisposable AcquireBusLock()
        {
            BusLockAcquisitions++;
            return NoOpDisposable.Instance;
        }

        private sealed class NoOpDisposable : IDisposable
        {
            public static readonly NoOpDisposable Instance = new();
            public void Dispose() { }
        }

        public void SeedFuryDimm(byte address, string signature, byte modelCode)
        {
            _presentAddresses.Add(address);
            for (var i = 0; i < 4; i++)
            {
                _words[(address, (byte)(KingstonFuryDramProtocol.RegisterSignatureStart + i))] = (ushort)(signature[i] << 8);
            }

            _words[(address, KingstonFuryDramProtocol.RegisterModel)] = (ushort)(modelCode << 8);
        }

        public bool TryWriteByteData(byte address, byte register, byte value)
        {
            SmbusAddressFirewall.ValidateAddress(address);
            SmbusSafetyGuard.ValidateWrite(address, register, value, SmbusSafetyGuard.HardMaxAllowedAddress);
            if (!_presentAddresses.Contains(address))
            {
                return false;
            }

            Writes.Add((address, register, value));
            return true;
        }

        public bool TryReadWordData(byte address, byte register, out ushort value)
        {
            SmbusAddressFirewall.ValidateAddress(address);
            SmbusSafetyGuard.ValidateRead(address, register, SmbusSafetyGuard.HardMaxAllowedAddress);
            return _words.TryGetValue((address, register), out value);
        }

        public void Dispose()
        {
        }
    }

    private static byte[] BuildSmbiosType17Table(
        params (int SizeMb, ushort SpeedMt, string DeviceLocator, string BankLocator, string Manufacturer, string Serial, string PartNumber)[] entries)
    {
        var buffer = new List<byte>();
        ushort handle = 1;

        foreach (var entry in entries)
        {
            var header = new byte[0x20];
            header[0x00] = 17; // Type 17 (Memory Device)
            header[0x01] = (byte)header.Length;
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x02, 2), handle++);

            if (entry.SizeMb >= 0x7FFF)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x0C, 2), 0x7FFF);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x1C, 4), (uint)entry.SizeMb);
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x0C, 2), (ushort)entry.SizeMb);
            }

            header[0x10] = 1; // DeviceLocator
            header[0x11] = 2; // BankLocator
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x15, 2), entry.SpeedMt);
            header[0x17] = 3; // Manufacturer
            header[0x18] = 4; // SerialNumber
            header[0x1A] = 5; // PartNumber

            buffer.AddRange(header);
            foreach (var str in new[] { entry.DeviceLocator, entry.BankLocator, entry.Manufacturer, entry.Serial, entry.PartNumber })
            {
                buffer.AddRange(Encoding.ASCII.GetBytes(str));
                buffer.Add(0);
            }

            buffer.Add(0); // End of structure string-set
        }

        // Type 127 (End-of-Table)
        buffer.AddRange([127, 4, 0xFF, 0xFF, 0, 0]);
        return [.. buffer];
    }
}
