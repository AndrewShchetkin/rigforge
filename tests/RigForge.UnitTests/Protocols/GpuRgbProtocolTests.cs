using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform;
using RigForge.Infrastructure.CrossPlatform.Gpu;
using RigForge.Infrastructure.CrossPlatform.Gpu.Asus;
using RigForge.Infrastructure.CrossPlatform.Gpu.Gigabyte;
using RigForge.Infrastructure.CrossPlatform.Gpu.Msi;
using RigForge.Infrastructure.CrossPlatform.Manifests;

namespace RigForge.UnitTests.Protocols;

public sealed class GpuRgbProtocolTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "rigforge-gpu-tests-" + Guid.NewGuid().ToString("N"));

    public GpuRgbProtocolTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x08)]
    [InlineData(0x36)]
    [InlineData(0x48)]
    [InlineData(0x50)] // EDID / DDC
    [InlineData(0x53)] // EDID / DDC
    [InlineData(0x57)] // EDID / DDC
    [InlineData(0x5F)] // VRM / system ceiling
    [InlineData(0x78)]
    [InlineData(0x7F)]
    [InlineData(0xFF)]
    public void Gpu_firewall_blocks_edid_vrm_and_out_of_range_addresses(byte address)
    {
        Assert.False(GpuI2cAddressFirewall.IsAllowed(address));
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => GpuI2cAddressFirewall.ValidateSlaveAddress(address));
        Assert.Contains("0x50..0x57", ex.Message);
        Assert.Contains("0x60", ex.Message);
    }

    [Theory]
    [InlineData(0x60)]
    [InlineData(0x67)] // ASUS Aura GPU
    [InlineData(0x68)] // MSI Mystic GPU
    [InlineData(0x71)] // Gigabyte Gen2
    [InlineData(0x75)] // Gigabyte Gen4
    [InlineData(0x77)]
    public void Gpu_firewall_allows_rgb_controller_window(byte address)
    {
        Assert.True(GpuI2cAddressFirewall.IsAllowed(address));
        GpuI2cAddressFirewall.ValidateSlaveAddress(address);
    }

    [Fact]
    public void Gpu_firewall_window_constants_match_spec()
    {
        Assert.Equal(0x60, GpuI2cAddressFirewall.MinAllowedAddress);
        Assert.Equal(0x77, GpuI2cAddressFirewall.MaxAllowedAddress);
    }

    [Fact]
    public void Null_factory_reports_unavailable_without_buses()
    {
        IGpuI2cBusFactory factory = new NullGpuI2cBusFactory();

        Assert.False(factory.IsAvailable);
        Assert.Equal("None", factory.BackendName);
        Assert.Empty(factory.OpenBuses());
    }

    [Fact]
    public void Catalog_matches_asus_5070_ti_tuf()
    {
        var id = new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, 0x2C05, GpuPciIdentity.AsusSubVendorId, 0x89F4);

        var match = GpuPciCatalog.FindMatch(id);

        Assert.NotNull(match);
        Assert.Equal(DeviceProtocolFamily.AsusAuraGpuI2c, match.Protocol);
        Assert.Equal(0x67, match.I2cAddress);
        Assert.Equal("ASUS", match.Vendor);
        Assert.Equal("ASUS GeForce RTX 5070 Ti TUF GAMING", match.Name);
    }

    [Fact]
    public void Catalog_matches_msi_5070_ti_gaming_trio()
    {
        var id = new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, 0x2C05, GpuPciIdentity.MsiSubVendorId, 0x5315);

        var match = GpuPciCatalog.FindMatch(id);

        Assert.NotNull(match);
        Assert.Equal(DeviceProtocolFamily.MsiMysticGpuI2c, match.Protocol);
        Assert.Equal(0x68, match.I2cAddress);
        Assert.Equal("Blackwell:GAMING TRIO", match.Variant);
    }

    [Fact]
    public void Catalog_matches_gigabyte_5070_ti_gaming_oc()
    {
        var id = new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, 0x2C05, GpuPciIdentity.GigabyteSubVendorId, 0x4181);

        var match = GpuPciCatalog.FindMatch(id);

        Assert.NotNull(match);
        Assert.Equal(DeviceProtocolFamily.GigabyteFusionGpuI2c, match.Protocol);
        Assert.Equal(0x75, match.I2cAddress);
        Assert.Equal("Gen4:GAMING", match.Variant);
    }

    [Fact]
    public void Catalog_returns_gen4_then_gen2_candidates_for_gigabyte_aorus_master()
    {
        var id = new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, 0x2F04, GpuPciIdentity.GigabyteSubVendorId, 0x4175);

        var matches = GpuPciCatalog.FindMatches(id);

        Assert.Equal(2, matches.Count);
        Assert.Equal(0x75, matches[0].I2cAddress);
        Assert.Equal("Gen4:AORUS MASTER", matches[0].Variant);
        Assert.Equal(0x71, matches[1].I2cAddress);
        Assert.Equal("Gen2:GAMING_GEN2", matches[1].Variant);
        Assert.Equal(0x75, GpuPciCatalog.FindMatch(id)!.I2cAddress);
    }

    [Fact]
    public void Catalog_returns_nothing_for_unknown_subsystem_or_non_nvidia_vendor()
    {
        Assert.Null(GpuPciCatalog.FindMatch(new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, 0x2C05, GpuPciIdentity.AsusSubVendorId, 0xFFFF)));
        Assert.Empty(GpuPciCatalog.FindMatches(new GpuPciIdentity(0x1002, 0x2C05, GpuPciIdentity.AsusSubVendorId, 0x89F4)));
    }

    [Fact]
    public void Catalog_entries_only_use_firewall_approved_addresses_and_are_unique()
    {
        Assert.NotEmpty(GpuPciCatalog.All);
        Assert.All(GpuPciCatalog.All, e => Assert.True(GpuI2cAddressFirewall.IsAllowed(e.I2cAddress), e.Name));
        Assert.Equal(GpuPciCatalog.All.Count, GpuPciCatalog.All.Select(e => (e.PciId, e.I2cAddress)).Distinct().Count());
        Assert.All(GpuPciCatalog.All, e => Assert.Equal(GpuPciIdentity.NvidiaVendorId, e.PciId.VendorId));
    }

    [Fact]
    public void Catalog_covers_requested_asus_msi_and_gigabyte_families()
    {
        Assert.Contains(GpuPciCatalog.All, e => e.Protocol == DeviceProtocolFamily.AsusAuraGpuI2c && e.PciId.DeviceId == 0x2B85);
        Assert.Contains(GpuPciCatalog.All, e => e.Protocol == DeviceProtocolFamily.AsusAuraGpuI2c && e.PciId.DeviceId == 0x2684);
        Assert.Contains(GpuPciCatalog.All, e => e.Protocol == DeviceProtocolFamily.AsusAuraGpuI2c && e.PciId.DeviceId == 0x2204);
        Assert.Contains(GpuPciCatalog.All, e => e.Variant is not null && e.Variant.StartsWith("Lovelace:", StringComparison.Ordinal));
        Assert.Contains(GpuPciCatalog.All, e => e.Variant == "Gen2:SINGLE_GEN2");
    }

    [Fact]
    public void Manifest_catalog_keeps_gpu_manifests_with_different_subdevices_separate()
    {
        File.WriteAllText(Path.Combine(_directory, "gpus.json"), """
            [
              { "id": "gpu-a", "name": "Card A", "vendor": "ASUS", "protocol": "AsusAuraGpuI2c",
                "vendorId": 4318, "productId": 11269, "subVendorId": 4163, "subDeviceId": 35316, "i2cAddress": 103 },
              { "id": "gpu-b", "name": "Card B", "vendor": "ASUS", "protocol": "AsusAuraGpuI2c",
                "vendorId": 4318, "productId": 11269, "subVendorId": 4163, "subDeviceId": 35340, "i2cAddress": 103, "variant": "TUF" }
            ]
            """);

        var catalog = new JsonDeviceManifestCatalog(_directory);

        var gpus = catalog.ForProtocol(DeviceProtocolFamily.AsusAuraGpuI2c);
        Assert.Equal(2, gpus.Count);
        Assert.Contains(gpus, m => m.Id == "gpu-a" && m.SubDeviceId == 0x89F4 && m.I2cAddress == 0x67);
        Assert.Contains(gpus, m => m.Id == "gpu-b" && m.Variant == "TUF");
    }

    [Fact]
    public void Manifest_catalog_upserts_same_gpu_subdevice_and_never_exposes_gpus_as_usb()
    {
        File.WriteAllText(Path.Combine(_directory, "a.json"), """
            { "id": "gpu-old", "name": "Old", "vendor": "ASUS", "protocol": "AsusAuraGpuI2c",
              "vendorId": 4318, "productId": 11269, "subVendorId": 4163, "subDeviceId": 35316, "i2cAddress": 103 }
            """);
        File.WriteAllText(Path.Combine(_directory, "b.json"), """
            { "id": "gpu-new", "name": "New", "vendor": "ASUS", "protocol": "AsusAuraGpuI2c",
              "vendorId": 4318, "productId": 11269, "subVendorId": 4163, "subDeviceId": 35316, "i2cAddress": 104 }
            """);

        var catalog = new JsonDeviceManifestCatalog(_directory);

        var gpu = Assert.Single(catalog.ForProtocol(DeviceProtocolFamily.AsusAuraGpuI2c));
        Assert.Equal("gpu-new", gpu.Id);
        Assert.Null(catalog.FindUsb(0x10DE, 0x2C05));
    }

    [Fact]
    public void Manifest_catalog_still_resolves_builtin_usb_devices()
    {
        var catalog = new JsonDeviceManifestCatalog(_directory);

        Assert.NotNull(catalog.FindUsb(0x0B05, 0x19AF));
    }

    [Fact]
    public void Custom_manifest_takes_precedence_over_builtin_catalog_entry()
    {
        File.WriteAllText(Path.Combine(_directory, "override.json"), """
            { "id": "custom-asus", "name": "Custom TUF", "vendor": "ASUS", "protocol": "AsusAuraGpuI2c",
              "vendorId": 4318, "productId": 11269, "subVendorId": 4163, "subDeviceId": 35316, "i2cAddress": 104 }
            """);
        var catalog = new JsonDeviceManifestCatalog(_directory);
        var id = new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, 0x2C05, GpuPciIdentity.AsusSubVendorId, 0x89F4);

        var match = GpuPciCatalog.FindMatch(id, catalog);

        Assert.NotNull(match);
        Assert.Equal("Custom TUF", match.Name);
        Assert.Equal(0x68, match.I2cAddress);
    }

    [Fact]
    public void Custom_manifest_with_blocked_address_is_ignored()
    {
        File.WriteAllText(Path.Combine(_directory, "bad.json"), """
            { "id": "bad", "name": "Bad", "vendor": "ASUS", "protocol": "AsusAuraGpuI2c",
              "vendorId": 4318, "productId": 11269, "subVendorId": 4163, "subDeviceId": 35316, "i2cAddress": 80 }
            """);
        var catalog = new JsonDeviceManifestCatalog(_directory);
        var id = new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, 0x2C05, GpuPciIdentity.AsusSubVendorId, 0x89F4);

        var match = GpuPciCatalog.FindMatch(id, catalog);

        Assert.NotNull(match);
        Assert.Equal(0x67, match.I2cAddress);
        Assert.NotEqual("Bad", match.Name);
    }

    [Fact]
    public void Custom_manifest_enables_card_missing_from_builtin_catalog()
    {
        File.WriteAllText(Path.Combine(_directory, "new.json"), """
            { "id": "new-card", "name": "New MSI", "vendor": "MSI", "protocol": "MsiMysticGpuI2c",
              "vendorId": 4318, "productId": 11269, "subVendorId": 5218, "subDeviceId": 65535, "i2cAddress": 104 }
            """);
        var catalog = new JsonDeviceManifestCatalog(_directory);
        var id = new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, 0x2C05, GpuPciIdentity.MsiSubVendorId, 0xFFFF);

        var match = GpuPciCatalog.FindMatch(id, catalog);

        Assert.NotNull(match);
        Assert.Equal(DeviceProtocolFamily.MsiMysticGpuI2c, match.Protocol);
        Assert.Equal("New MSI", match.Name);
    }

    [Fact]
    public void Hardware_options_enable_gpu_rgb_by_default()
    {
        Assert.True(new HardwareOptions().GpuRgb.Enabled);
    }

    [Fact]
    public void Conflicting_software_detector_lists_signalrgb_service_and_rgb_fusion()
    {
        var field = typeof(RigForge.Infrastructure.CrossPlatform.Diagnostics.ConflictingSoftwareDetector)
            .GetField("KnownConflicts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        var entries = (IEnumerable<(string ProcessName, string Product, string Impact)>)field.GetValue(null)!;

        Assert.Contains(entries, e => e.ProcessName == "SignalRgbService");
        Assert.Contains(entries, e => e.ProcessName == "RGBFusion");
    }

    // ---------------------------------------------------------------- catalog accuracy polish

    [Theory]
    [InlineData(0x2C05, 0x5314, "Blackwell:VANGUARD")]
    [InlineData(0x2C05, 0x5315, "Blackwell:GAMING TRIO")]
    [InlineData(0x2C02, 0x5313, "Blackwell:SUPRIM SOC")]
    [InlineData(0x2C02, 0x5314, "Blackwell:VANGUARD")]
    [InlineData(0x2C02, 0x5315, "Blackwell:GAMING TRIO")]
    [InlineData(0x2B85, 0x5301, "Blackwell:SUPRIM SOC")]
    [InlineData(0x2B85, 0x5302, "Blackwell:VANGUARD")]
    [InlineData(0x2B85, 0x5303, "Blackwell:GAMING TRIO")]
    public void Catalog_msi_blackwell_variants_follow_subsystem_ids(int deviceId, int subDeviceId, string variant)
    {
        var match = GpuPciCatalog.FindMatch(new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, (ushort)deviceId, GpuPciIdentity.MsiSubVendorId, (ushort)subDeviceId));

        Assert.NotNull(match);
        Assert.Equal(variant, match.Variant);
    }

    [Fact]
    public void Catalog_lists_asus_5060_ti_tuf_8a08_and_exposes_a_read_only_list()
    {
        var match = GpuPciCatalog.FindMatch(new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, 0x2D04, GpuPciIdentity.AsusSubVendorId, 0x8A08));

        Assert.NotNull(match);
        Assert.Equal(DeviceProtocolFamily.AsusAuraGpuI2c, match.Protocol);
        Assert.True(((System.Collections.Generic.ICollection<GpuCatalogEntry>)GpuPciCatalog.All).IsReadOnly);
    }

    // ---------------------------------------------------------------- ASUS Aura GPU

    [Fact]
    public void Asus_signature_accepts_auma_and_aura_prefixes_only()
    {
        Assert.True(AsusAuraGpuProtocol.IsAuraGpuSignature("AUMA0-E6K5-1107"));
        Assert.True(AsusAuraGpuProtocol.IsAuraGpuSignature("AURA-GPU"));
        Assert.False(AsusAuraGpuProtocol.IsAuraGpuSignature("XXXX"));
        Assert.False(AsusAuraGpuProtocol.IsAuraGpuSignature(""));
        Assert.False(AsusAuraGpuProtocol.IsAuraGpuSignature("AUM"));
    }

    [Fact]
    public async Task Asus_aura_gpu_reads_identity_enables_direct_mode_and_reverses_led_x()
    {
        var entry = Entry(0x2C05, GpuPciIdentity.AsusSubVendorId, 0x89F4);
        var bus = AsusBus(entry.PciId, "AUMA0-E6K5-1107", 4);

        await using var device = AsusAuraGpuDevice.TryOpen(bus, entry, dryRun: false);

        Assert.NotNull(device);
        Assert.Equal("asus-gpu-i2c:nvapi-gpu-0-i2c-1:67", device.Id);
        Assert.Equal("ASUS GeForce RTX 5070 Ti TUF GAMING", device.Name);
        Assert.Equal("ASUS", device.Vendor);
        Assert.Equal(DeviceCapabilities.Rgb, device.Capabilities);
        Assert.Equal(30, device.MaxFramesPerSecond);
        Assert.Equal("NVAPI GPU 0 / I2C 1", device.Properties["Bus"]);
        Assert.Equal("10DE:2C05 (1043:89F4)", device.Properties["PciId"]);
        Assert.Equal("0x67", device.Properties["Address"]);
        Assert.Equal("AUMA0-E6K5-1107", device.Properties["Controller"]);
        Assert.Equal("4", device.Properties["LedCount"]);

        var zone = Assert.Single(device.Zones);
        Assert.Equal("gpu", zone.Id);
        Assert.Equal("GPU RGB", zone.Name);
        Assert.Equal(LedTopology.Linear, zone.Topology);
        Assert.True(zone.IsResizable);
        Assert.Equal(30, zone.MaxLedCount);
        Assert.Equal([1.0f, 2f / 3f, 1f / 3f, 0.0f], zone.Leds.Select(l => l.X).ToArray());
        Assert.All(zone.Leds, l => Assert.Equal(0.5f, l.Y));

        Assert.Equal(1, bus.AuraRegs[0x8020]);
        Assert.Equal(1, bus.AuraRegs[0x80A0]);
        Assert.Equal(["W:00=8020", "W:01=01", "W:00=80A0", "W:01=01"], bus.Calls.TakeLast(4).Select(FakeGpuI2cBus.Describe).ToArray());
        Assert.False(bus.Disposed);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(31, 30)]
    [InlineData(1, 1)]
    [InlineData(30, 30)]
    public async Task Asus_aura_gpu_falls_back_to_30_leds_when_count_is_out_of_range(int raw, int expected)
    {
        var entry = Entry(0x2C05, GpuPciIdentity.AsusSubVendorId, 0x89F4);
        var bus = AsusBus(entry.PciId, "AUMA0-E6K5-1107", raw);

        await using var device = AsusAuraGpuDevice.TryOpen(bus, entry, dryRun: true);

        Assert.NotNull(device);
        Assert.Equal(expected, Assert.Single(device.Zones).LedCount);
    }

    [Fact]
    public void Asus_aura_gpu_open_fails_cleanly_when_controller_name_cannot_be_read()
    {
        var entry = Entry(0x2C05, GpuPciIdentity.AsusSubVendorId, 0x89F4);
        var bus = AsusBus(entry.PciId, "AUMA0-E6K5-1107", 4);
        bus.FailReads = true;

        var device = AsusAuraGpuDevice.TryOpen(bus, entry, dryRun: false);

        Assert.Null(device);
        Assert.Empty(bus.StateChangingWrites);
        Assert.False(bus.Disposed); // ownership stays with the caller when open fails
    }

    [Fact]
    public void Asus_aura_gpu_rejects_firewall_blocked_addresses()
    {
        var entry = Entry(0x2C05, GpuPciIdentity.AsusSubVendorId, 0x89F4) with { I2cAddress = 0x50 };
        var bus = AsusBus(entry.PciId, "AUMA0-E6K5-1107", 4);

        Assert.Throws<ArgumentOutOfRangeException>(() => AsusAuraGpuDevice.TryOpen(bus, entry, dryRun: false));
        Assert.Empty(bus.Calls);
    }

    [Fact]
    public async Task Asus_aura_gpu_sends_rbg_ordered_frame_and_skips_unchanged_frames()
    {
        var entry = Entry(0x2C05, GpuPciIdentity.AsusSubVendorId, 0x89F4);
        var bus = AsusBus(entry.PciId, "AUMA0-E6K5-1107", 4);
        await using var device = AsusAuraGpuDevice.TryOpen(bus, entry, dryRun: false)!;
        bus.Calls.Clear();
        RgbColor[] colors = [new(1, 2, 3), new(4, 5, 6), new(7, 8, 9), new(10, 11, 12)];

        await device.UpdateLedsAsync(colors, CancellationToken.None);

        Assert.Equal(
            ["W:00=8100", "W:03=0C" + "010302" + "040605" + "070908" + "0A0C0B"],
            bus.Calls.Select(FakeGpuI2cBus.Describe).ToArray());

        var callsAfterFirstFrame = bus.Calls.Count;
        var locksAfterFirstFrame = bus.LockCount;
        await device.UpdateLedsAsync(colors, CancellationToken.None);

        Assert.Equal(callsAfterFirstFrame, bus.Calls.Count);
        Assert.Equal(locksAfterFirstFrame, bus.LockCount);

        await device.UpdateLedsAsync(new RgbColor[] { new(1, 2, 3), new(4, 5, 6), new(7, 8, 9), new(10, 11, 13) }, CancellationToken.None);
        Assert.True(bus.Calls.Count > callsAfterFirstFrame);
    }

    [Fact]
    public async Task Asus_aura_gpu_chunks_thirty_leds_into_31_31_28_byte_blocks()
    {
        var entry = Entry(0x2C05, GpuPciIdentity.AsusSubVendorId, 0x89F4);
        var bus = AsusBus(entry.PciId, "AUMA0-E6K5-1107", 30);
        await using var device = AsusAuraGpuDevice.TryOpen(bus, entry, dryRun: false)!;
        bus.Calls.Clear();

        await device.UpdateLedsAsync(Palette(30, 1), CancellationToken.None);

        var blocks = bus.Calls.Where(c => c.Kind == "W" && c.Register.SequenceEqual(new byte[] { 0x03 })).ToArray();
        Assert.Equal([32, 32, 29], blocks.Select(b => b.Data.Length).ToArray());
        Assert.Equal([31, 31, 28], blocks.Select(b => (int)b.Data[0]).ToArray());
        Assert.Equal(90, blocks.Sum(b => b.Data[0]));
        Assert.Equal(["W:00=8100"], bus.Calls.Take(1).Select(FakeGpuI2cBus.Describe).ToArray());
    }

    [Fact]
    public async Task Asus_aura_gpu_retries_the_frame_when_a_write_fails()
    {
        var entry = Entry(0x2C05, GpuPciIdentity.AsusSubVendorId, 0x89F4);
        var bus = AsusBus(entry.PciId, "AUMA0-E6K5-1107", 4);
        await using var device = AsusAuraGpuDevice.TryOpen(bus, entry, dryRun: false)!;
        var colors = Palette(4, 9);

        bus.FailWriteAddresses.Add(0x67);
        await Assert.ThrowsAsync<IOException>(async () => await device.UpdateLedsAsync(colors, CancellationToken.None));

        bus.FailWriteAddresses.Clear();
        bus.Calls.Clear();
        await device.UpdateLedsAsync(colors, CancellationToken.None);

        Assert.Equal(2, bus.Calls.Count);
    }

    [Fact]
    public async Task Asus_aura_gpu_dry_run_never_writes_state_on_init_update_or_dispose()
    {
        var entry = Entry(0x2C05, GpuPciIdentity.AsusSubVendorId, 0x89F4);
        var bus = AsusBus(entry.PciId, "AUMA0-E6K5-1107", 4);

        var device = AsusAuraGpuDevice.TryOpen(bus, entry, dryRun: true)!;
        await device.UpdateLedsAsync(Palette(4, 3), CancellationToken.None);
        await device.DisposeAsync();

        Assert.Empty(bus.StateChangingWrites);
        Assert.DoesNotContain(bus.Calls, c => c.Kind == "W" && !FakeGpuI2cBus.IsPointerSelect(c));
        Assert.True(bus.Disposed);
    }

    [Fact]
    public async Task Asus_aura_gpu_resizes_zone_with_reversed_x_and_invalidates_the_diff_cache()
    {
        var entry = Entry(0x2C05, GpuPciIdentity.AsusSubVendorId, 0x89F4);
        var bus = AsusBus(entry.PciId, "AUMA0-E6K5-1107", 4);
        await using var device = AsusAuraGpuDevice.TryOpen(bus, entry, dryRun: false)!;
        var before = device.Zones;
        await device.UpdateLedsAsync(Palette(4, 5), CancellationToken.None);

        Assert.False(device.TryResizeZone("gpu", 0));
        Assert.False(device.TryResizeZone("gpu", 31));
        Assert.False(device.TryResizeZone("other", 8));
        Assert.True(device.TryResizeZone("gpu", 1));
        Assert.Equal([0.5f], Assert.Single(device.Zones).Leds.Select(l => l.X).ToArray());
        Assert.True(device.TryResizeZone("gpu", 5));

        var zone = Assert.Single(device.Zones);
        Assert.NotSame(before, device.Zones);
        Assert.Equal([1.0f, 0.75f, 0.5f, 0.25f, 0.0f], zone.Leds.Select(l => l.X).ToArray());
        Assert.Equal("5", device.Properties["LedCount"]);

        bus.Calls.Clear();
        await device.UpdateLedsAsync(Palette(5, 5), CancellationToken.None);
        Assert.NotEmpty(bus.Calls);
    }

    [Fact]
    public async Task Asus_aura_gpu_restores_hardware_rainbow_on_dispose_without_flash_commands()
    {
        var entry = Entry(0x2C05, GpuPciIdentity.AsusSubVendorId, 0x89F4);
        var bus = AsusBus(entry.PciId, "AUMA0-E6K5-1107", 4);
        var device = AsusAuraGpuDevice.TryOpen(bus, entry, dryRun: false)!;
        bus.Calls.Clear();

        await device.DisposeAsync();
        await device.DisposeAsync(); // idempotent

        Assert.Equal(
            ["W:00=8020", "W:01=00", "W:00=8020", "W:02=0005", "W:00=8024", "W:01=00", "W:00=802F", "W:01=01"],
            bus.Calls.Select(FakeGpuI2cBus.Describe).ToArray());
        Assert.True(bus.Disposed);
        Assert.Equal(1, bus.DisposeCount);

        // Only the documented volatile registers are ever addressed; there is never a raw (flash/EEPROM) command.
        Assert.DoesNotContain(bus.Calls, c => c.Kind == "RAW");
        Assert.All(bus.PointerTargets, t => Assert.Contains(t, new ushort[] { 0x8020, 0x80A0, 0x8100, 0x8024, 0x802F, 0x1C03 }.Concat(Enumerable.Range(0x1000, 16).Select(x => (ushort)x))));
    }

    [Fact]
    public async Task Asus_aura_gpu_dispose_survives_i2c_errors()
    {
        var entry = Entry(0x2C05, GpuPciIdentity.AsusSubVendorId, 0x89F4);
        var bus = AsusBus(entry.PciId, "AUMA0-E6K5-1107", 4);
        var device = AsusAuraGpuDevice.TryOpen(bus, entry, dryRun: false)!;
        bus.FailWriteAddresses.Add(0x67);

        await device.DisposeAsync();

        Assert.True(bus.Disposed);
    }

    // ---------------------------------------------------------------- MSI Mystic Light GPU

    [Fact]
    public async Task Msi_blackwell_gaming_trio_inits_and_streams_five_byte_led_records()
    {
        var entry = Entry(0x2C05, GpuPciIdentity.MsiSubVendorId, 0x5315);
        var bus = new FakeGpuI2cBus("NVAPI/GPU-0/I2C-1", entry.PciId);

        var device = MsiMysticGpuDevice.TryOpen(bus, entry, dryRun: false);

        Assert.NotNull(device);
        Assert.Equal("msi-gpu-i2c:nvapi-gpu-0-i2c-1:68", device.Id);
        Assert.Equal("MSI", device.Vendor);
        Assert.Equal(30, device.MaxFramesPerSecond);
        Assert.Equal(6, Assert.Single(device.Zones).LedCount);
        Assert.Equal(["W:46=01", "W:06=00", "W:20=00"], bus.Calls.Select(FakeGpuI2cBus.Describe).ToArray());
        bus.Calls.Clear();

        var colors = Palette(6, 20);
        await device.UpdateLedsAsync(colors, CancellationToken.None);

        var call = Assert.Single(bus.Calls);
        Assert.Equal(0x68, call.Slave);
        Assert.Equal(new byte[] { 0x04 }, call.Register);
        Assert.Equal(31, call.Data.Length);
        Assert.Equal(0x01, call.Data[0]);
        for (var i = 0; i < 6; i++)
        {
            var offset = 1 + i * 5;
            Assert.Equal(
                new byte[] { (byte)i, colors[i].R, colors[i].G, colors[i].B, 0x01 },
                call.Data.AsSpan(offset, 5).ToArray());
        }

        await device.UpdateLedsAsync(colors, CancellationToken.None);
        Assert.Single(bus.Calls); // unchanged frame: 0 writes

        // Dispose pushes one volatile all-black frame (no flash save) and releases the bus.
        bus.Calls.Clear();
        await device.DisposeAsync();
        var black = Assert.Single(bus.Calls);
        Assert.Equal(new byte[] { 0x04 }, black.Register);
        Assert.Equal(0x01, black.Data[0]);
        Assert.All(Enumerable.Range(0, 6), i => Assert.Equal(new byte[] { (byte)i, 0, 0, 0, 0x01 }, black.Data.AsSpan(1 + i * 5, 5).ToArray()));
        Assert.True(bus.Disposed);
    }

    [Fact]
    public async Task Msi_blackwell_vanguard_uses_hardware_index_remap()
    {
        var entry = Entry(0x2C05, GpuPciIdentity.MsiSubVendorId, 0x5314);
        Assert.Equal("Blackwell:VANGUARD", entry.Variant);
        var bus = new FakeGpuI2cBus("NVAPI/GPU-0/I2C-1", entry.PciId);
        await using var device = MsiMysticGpuDevice.TryOpen(bus, entry, dryRun: false)!;
        Assert.Equal(23, Assert.Single(device.Zones).LedCount);
        bus.Calls.Clear();
        var colors = Palette(23, 40);

        await device.UpdateLedsAsync(colors, CancellationToken.None);

        var packet = Assert.Single(bus.Calls).Data;
        Assert.Equal((22 + 1) * 5 + 1, packet.Length);
        // LED 8 is wired to hardware index 11; LED 13 to 18.
        Assert.Equal(new byte[] { 11, colors[8].R, colors[8].G, colors[8].B, 0x01 }, packet.AsSpan(1 + 11 * 5, 5).ToArray());
        Assert.Equal(new byte[] { 18, colors[13].R, colors[13].G, colors[13].B, 0x01 }, packet.AsSpan(1 + 18 * 5, 5).ToArray());
    }

    [Fact]
    public async Task Msi_lovelace_inits_five_registers_and_streams_136_byte_bgr0_frames()
    {
        var entry = Entry(0x2684, GpuPciIdentity.MsiSubVendorId, 0x5103);
        Assert.Equal("Lovelace:GAMING TRIO", entry.Variant);
        var bus = new FakeGpuI2cBus("NVAPI/GPU-0/I2C-1", entry.PciId);
        var device = MsiMysticGpuDevice.TryOpen(bus, entry, dryRun: false)!;
        Assert.Equal(["W:2D=00", "W:46=01", "W:22=1C", "W:2E=01", "W:38=04"], bus.Calls.Select(FakeGpuI2cBus.Describe).ToArray());
        bus.Calls.Clear();
        var colors = Palette(6, 60);

        await device.UpdateLedsAsync(colors, CancellationToken.None);

        var call = Assert.Single(bus.Calls);
        Assert.Equal(new byte[] { 0x49 }, call.Register);
        Assert.Equal(136, call.Data.Length);
        for (var i = 0; i < 6; i++)
        {
            Assert.Equal(new byte[] { colors[i].B, colors[i].G, colors[i].R, 0x00 }, call.Data.AsSpan(i * 4, 4).ToArray());
        }

        await device.UpdateLedsAsync(colors, CancellationToken.None);
        Assert.Single(bus.Calls);
        await device.DisposeAsync();
        Assert.True(bus.Disposed);
    }

    [Fact]
    public async Task Msi_suprim_has_fifteen_leds_and_unknown_variant_is_resizable()
    {
        var suprim = Entry(0x2C02, GpuPciIdentity.MsiSubVendorId, 0x5313);
        await using var suprimDevice = MsiMysticGpuDevice.TryOpen(new FakeGpuI2cBus("b", suprim.PciId), suprim, dryRun: true)!;
        Assert.Equal(15, Assert.Single(suprimDevice.Zones).LedCount);
        Assert.False(suprimDevice.TryResizeZone("gpu", 8));

        var custom = suprim with { Variant = "Blackwell:UNKNOWN" };
        await using var customDevice = MsiMysticGpuDevice.TryOpen(new FakeGpuI2cBus("b", suprim.PciId), custom, dryRun: true)!;
        Assert.Equal(6, Assert.Single(customDevice.Zones).LedCount);
        Assert.True(customDevice.TryResizeZone("gpu", 34));
        Assert.False(customDevice.TryResizeZone("gpu", 35));
        Assert.Equal(34, Assert.Single(customDevice.Zones).LedCount);
    }

    [Fact]
    public async Task Msi_dry_run_never_writes()
    {
        var entry = Entry(0x2C05, GpuPciIdentity.MsiSubVendorId, 0x5315);
        var bus = new FakeGpuI2cBus("b", entry.PciId);

        var device = MsiMysticGpuDevice.TryOpen(bus, entry, dryRun: true)!;
        await device.UpdateLedsAsync(Palette(6, 1), CancellationToken.None);
        await device.DisposeAsync();

        Assert.Empty(bus.Calls);
        Assert.True(bus.Disposed);
    }

    // ---------------------------------------------------------------- Gigabyte RGB Fusion GPU

    [Fact]
    public async Task Gigabyte_gen4_gaming_initializes_unmapped_zones_and_diffs_per_zone()
    {
        var entry = Entry(0x2C05, GpuPciIdentity.GigabyteSubVendorId, 0x4181);
        var bus = new FakeGpuI2cBus("NVAPI/GPU-0/I2C-1", entry.PciId);
        var timer = new RecordingTimer();

        var device = GigabyteFusionGpuDevice.TryOpen(bus, entry, dryRun: false, timer);

        Assert.NotNull(device);
        Assert.Equal("gigabyte-gpu-i2c:nvapi-gpu-0-i2c-1:75", device.Id);
        Assert.Equal("Gigabyte", device.Vendor);
        Assert.Equal(30, device.MaxFramesPerSecond);
        Assert.Equal(["Fan 1", "Fan 2", "Fan 3", "Logo"], device.Zones.Select(z => z.Name).ToArray());
        Assert.All(device.Zones, z => Assert.Equal(1, z.LedCount));
        Assert.Equal(
            ["RAW:=12010106" + "0A" + "00000000" + "04", "RAW:=12010106" + "0A" + "00000000" + "05"],
            bus.Calls.Select(FakeGpuI2cBus.Describe).ToArray());
        Assert.Equal([TimeSpan.FromMilliseconds(9)], timer.Sleeps);
        bus.Calls.Clear();
        timer.Sleeps.Clear();

        RgbColor[] colors = [new(1, 2, 3), new(4, 5, 6), new(7, 8, 9), new(10, 11, 12)];
        await device.UpdateLedsAsync(colors, CancellationToken.None);

        // Mapped hardware zone ids for GAMING are [1, 2, 0, 3].
        Assert.Equal(
            [
                "RAW:=120101060A" + "010203" + "00" + "01",
                "RAW:=120101060A" + "040506" + "00" + "02",
                "RAW:=120101060A" + "070809" + "00" + "00",
                "RAW:=120101060A" + "0A0B0C" + "00" + "03",
            ],
            bus.Calls.Select(FakeGpuI2cBus.Describe).ToArray());
        Assert.Equal(3, timer.Sleeps.Count);

        bus.Calls.Clear();
        await device.UpdateLedsAsync(colors, CancellationToken.None);
        Assert.Empty(bus.Calls);

        colors[2] = new RgbColor(70, 80, 90);
        await device.UpdateLedsAsync(colors, CancellationToken.None);
        Assert.Equal(["RAW:=120101060A" + "46505A" + "00" + "00"], bus.Calls.Select(FakeGpuI2cBus.Describe).ToArray());

        await device.DisposeAsync();
        Assert.True(bus.Disposed);
    }

    [Fact]
    public async Task Gigabyte_gen4_eagle_uses_the_0x16_header_and_single_zone()
    {
        var entry = Entry(0x2C05, GpuPciIdentity.GigabyteSubVendorId, 0x4180);
        Assert.Equal("Gen4:EAGLE", entry.Variant);
        var bus = new FakeGpuI2cBus("NVAPI/GPU-0/I2C-1", entry.PciId);
        await using var device = GigabyteFusionGpuDevice.TryOpen(bus, entry, dryRun: false, new RecordingTimer())!;
        Assert.Equal(1, Assert.Single(device.Zones).LedCount);
        Assert.Equal(5, bus.Calls.Count); // unmapped hardware zones 1..5
        bus.Calls.Clear();

        await device.UpdateLedsAsync(new[] { new RgbColor(9, 8, 7) }, CancellationToken.None);

        Assert.Equal(["RAW:=1601000600" + "090807" + "00" + "00"], bus.Calls.Select(FakeGpuI2cBus.Describe).ToArray());
    }

    [Fact]
    public async Task Gigabyte_gen4_aorus_master_and_waterforce_expose_their_zone_maps()
    {
        var master = Entry(0x2C05, GpuPciIdentity.GigabyteSubVendorId, 0x417B);
        await using var masterDevice = GigabyteFusionGpuDevice.TryOpen(new FakeGpuI2cBus("b", master.PciId), master, dryRun: true, new RecordingTimer())!;
        Assert.Equal(["Fan 1", "Fan 2", "Fan 3", "Logo", "Back Logo"], masterDevice.Zones.Select(z => z.Name).ToArray());

        var waterforce = Entry(0x2B85, GpuPciIdentity.GigabyteSubVendorId, 0x4171);
        await using var waterforceDevice = GigabyteFusionGpuDevice.TryOpen(new FakeGpuI2cBus("b", waterforce.PciId), waterforce, dryRun: true, new RecordingTimer())!;
        Assert.Equal(["Logo", "Fans", "Front Text", "Side Text"], waterforceDevice.Zones.Select(z => z.Name).ToArray());

        var xtreme = Entry(0x2C02, GpuPciIdentity.GigabyteSubVendorId, 0x418A);
        await using var xtremeDevice = GigabyteFusionGpuDevice.TryOpen(new FakeGpuI2cBus("b", xtreme.PciId), xtreme, dryRun: true, new RecordingTimer())!;
        Assert.Equal(["Logo", "Front Text", "Fans", "Accent", "Side Text"], xtremeDevice.Zones.Select(z => z.Name).ToArray());
    }

    [Fact]
    public void Gigabyte_gen4_open_fails_when_the_controller_does_not_acknowledge()
    {
        var entry = Entry(0x2C05, GpuPciIdentity.GigabyteSubVendorId, 0x4181);
        var bus = new FakeGpuI2cBus("b", entry.PciId);
        bus.FailWriteAddresses.Add(0x75);

        Assert.Null(GigabyteFusionGpuDevice.TryOpen(bus, entry, dryRun: false, new RecordingTimer()));
        Assert.False(bus.Disposed);
    }

    [Fact]
    public async Task Gigabyte_gen2_probes_inits_static_mode_and_streams_eight_byte_packets()
    {
        var gen2 = GpuPciCatalog.FindMatches(new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, 0x2C02, GpuPciIdentity.GigabyteSubVendorId, 0x4178))[1];
        Assert.Equal("Gen2:SINGLE_GEN2", gen2.Variant);
        var bus = new FakeGpuI2cBus("NVAPI/GPU-0/I2C-1", gen2.PciId);

        var device = GigabyteFusionGpuDevice.TryOpen(bus, gen2, dryRun: false, new RecordingTimer());

        Assert.NotNull(device);
        Assert.Equal("gigabyte-gpu-i2c:nvapi-gpu-0-i2c-1:71", device.Id);
        Assert.Equal(
            ["RAW:=AB00000000000000", "RAW:=880106630800"],
            bus.Calls.Select(FakeGpuI2cBus.Describe).ToArray());
        bus.Calls.Clear();

        var colors = new[] { new RgbColor(1, 2, 3) };
        await device.UpdateLedsAsync(colors, CancellationToken.None);
        Assert.Equal(["RAW:=B001010203010203"], bus.Calls.Select(FakeGpuI2cBus.Describe).ToArray());

        bus.Calls.Clear();
        await device.UpdateLedsAsync(colors, CancellationToken.None);
        Assert.Empty(bus.Calls);
        await device.DisposeAsync();
    }

    [Fact]
    public void Gigabyte_gen2_probe_accepts_ack_or_known_read_back_codes()
    {
        var pci = new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, 0x2C02, GpuPciIdentity.GigabyteSubVendorId, 0x4178);

        Assert.True(GigabyteFusionGpuProtocol.TryProbeGen2(new FakeGpuI2cBus("b", pci), 0x71));

        var readOnly = new FakeGpuI2cBus("b", pci) { RawReadResponse = [0xAB, 0x11, 0, 0, 0, 0, 0, 0] };
        readOnly.FailWriteAddresses.Add(0x71);
        Assert.True(GigabyteFusionGpuProtocol.TryProbeGen2(readOnly, 0x71));

        var wrongCode = new FakeGpuI2cBus("b", pci) { RawReadResponse = [0xAB, 0x13, 0, 0, 0, 0, 0, 0] };
        wrongCode.FailWriteAddresses.Add(0x71);
        Assert.False(GigabyteFusionGpuProtocol.TryProbeGen2(wrongCode, 0x71));

        var silent = new FakeGpuI2cBus("b", pci);
        silent.FailWriteAddresses.Add(0x71);
        Assert.False(GigabyteFusionGpuProtocol.TryProbeGen2(silent, 0x71));
    }

    [Fact]
    public async Task Gigabyte_dry_run_never_writes()
    {
        var entry = Entry(0x2C05, GpuPciIdentity.GigabyteSubVendorId, 0x4181);
        var bus = new FakeGpuI2cBus("b", entry.PciId);
        var timer = new RecordingTimer();

        var device = GigabyteFusionGpuDevice.TryOpen(bus, entry, dryRun: true, timer)!;
        await device.UpdateLedsAsync(Palette(4, 1), CancellationToken.None);
        await device.DisposeAsync();

        Assert.Empty(bus.Calls);
        Assert.Empty(timer.Sleeps);
        Assert.True(bus.Disposed);
    }

    // ---------------------------------------------------------------- zero allocations on warm frames

    [Fact]
    public async Task Warm_gpu_frames_allocate_nothing_and_unchanged_frames_never_touch_the_bus()
    {
        var asusEntry = Entry(0x2C05, GpuPciIdentity.AsusSubVendorId, 0x89F4);
        var asusBus = AsusBus(asusEntry.PciId, "AUMA0-E6K5-1107", 30);
        await using var asus = AsusAuraGpuDevice.TryOpen(asusBus, asusEntry, dryRun: false)!;

        var msiEntry = Entry(0x2C05, GpuPciIdentity.MsiSubVendorId, 0x5314);
        var msiBus = new FakeGpuI2cBus("m", msiEntry.PciId);
        await using var msi = MsiMysticGpuDevice.TryOpen(msiBus, msiEntry, dryRun: false)!;

        var lovelaceEntry = Entry(0x2684, GpuPciIdentity.MsiSubVendorId, 0x5103);
        var lovelaceBus = new FakeGpuI2cBus("l", lovelaceEntry.PciId);
        await using var lovelace = MsiMysticGpuDevice.TryOpen(lovelaceBus, lovelaceEntry, dryRun: false)!;

        var gigabyteEntry = Entry(0x2C05, GpuPciIdentity.GigabyteSubVendorId, 0x4181);
        var gigabyteBus = new FakeGpuI2cBus("g", gigabyteEntry.PciId);
        await using var gigabyte = GigabyteFusionGpuDevice.TryOpen(gigabyteBus, gigabyteEntry, dryRun: false, new NoopTimer())!;

        FakeGpuI2cBus[] buses = [asusBus, msiBus, lovelaceBus, gigabyteBus];
        IRgbDevice[] devices = [asus, msi, lovelace, gigabyte];
        var frameA = Palette(30, 1);
        var frameB = Palette(30, 99);

        foreach (var bus in buses)
        {
            bus.Record = false;
        }

        foreach (var device in devices)
        {
            await WarmAsync(device, frameA, frameB);
        }

        foreach (var device in devices)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 200; i++)
            {
                await device.UpdateLedsAsync(i % 2 == 0 ? frameA : frameB, CancellationToken.None);
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(allocated == 0, $"{device.Id} allocated {allocated} bytes");
        }

        Assert.All(buses, bus => Assert.True(bus.WriteCount > 0));

        foreach (var (device, bus) in devices.Zip(buses))
        {
            await device.UpdateLedsAsync(frameA, CancellationToken.None);
            var writes = bus.WriteCount;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++)
            {
                await device.UpdateLedsAsync(frameA, CancellationToken.None);
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            var writesAfter = bus.WriteCount;

            Assert.Equal(writes, writesAfter);
            Assert.Equal(0, allocated);
        }
    }

    // ---------------------------------------------------------------- GpuRgbProbe

    [Fact]
    public async Task Probe_matches_asus_5070_ti_tuf_by_pci_id_and_takes_ownership_of_the_bus()
    {
        var pci = new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, 0x2C05, GpuPciIdentity.AsusSubVendorId, 0x89F4);
        var bus = AsusBus(pci, "AUMA0-E6K5-1107", 4);
        var factory = new FakeGpuI2cBusFactory(bus);

        var devices = await NewProbe(factory).ProbeAsync([], CancellationToken.None);

        var device = Assert.IsAssignableFrom<IRgbDevice>(Assert.Single(devices));
        Assert.Equal("ASUS GeForce RTX 5070 Ti TUF GAMING", device.Name);
        Assert.Equal("asus-gpu-i2c:nvapi-gpu-0-i2c-1:67", device.Id);
        Assert.False(bus.Disposed);
        await device.DisposeAsync();
        Assert.True(bus.Disposed);
    }

    [Fact]
    public async Task Probe_name_and_disabled_or_unavailable_backend_return_nothing()
    {
        var pci = new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, 0x2C05, GpuPciIdentity.AsusSubVendorId, 0x89F4);
        var bus = AsusBus(pci, "AUMA0-E6K5-1107", 4);
        var factory = new FakeGpuI2cBusFactory(bus);

        Assert.Equal("GPU I2C RGB", NewProbe(factory).Name);
        Assert.Empty(await NewProbe(factory, enabled: false).ProbeAsync([], CancellationToken.None));
        factory.IsAvailable = false;
        Assert.Empty(await NewProbe(factory).ProbeAsync([], CancellationToken.None));
        Assert.Equal(0, factory.OpenCount);
        Assert.Empty(bus.Calls);
    }

    [Fact]
    public async Task Probe_accepts_unlisted_asus_card_with_aura_signature_and_rejects_others_without_writes()
    {
        var unlisted = new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, 0x2C05, GpuPciIdentity.AsusSubVendorId, 0xFFFF);
        var accepted = AsusBus(unlisted, "AUMA0-NEW", 8, adapterName: "NVIDIA GeForce RTX 9999");
        var rejected = AsusBus(unlisted, "XXXX", 8, busId: "NVAPI/GPU-1/I2C-1");
        var factory = new FakeGpuI2cBusFactory(accepted, rejected);

        var devices = await NewProbe(factory).ProbeAsync([], CancellationToken.None);

        var device = Assert.Single(devices);
        Assert.Equal("NVIDIA GeForce RTX 9999", device.Name);
        Assert.Equal("asus-gpu-i2c:nvapi-gpu-0-i2c-1:67", device.Id);
        Assert.False(accepted.Disposed);
        Assert.True(rejected.Disposed);
        Assert.Empty(rejected.StateChangingWrites);
        Assert.All(rejected.Calls, c => Assert.True(c.Kind == "R" || FakeGpuI2cBus.IsPointerSelect(c)));
        await device.DisposeAsync();
    }

    [Fact]
    public async Task Probe_unlisted_asus_card_without_adapter_name_uses_default_name()
    {
        var unlisted = new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, 0x2C05, GpuPciIdentity.AsusSubVendorId, 0xFFFF);
        var bus = AsusBus(unlisted, "AURA-X", 8, adapterName: "");

        var devices = await NewProbe(new FakeGpuI2cBusFactory(bus)).ProbeAsync([], CancellationToken.None);

        var device = Assert.Single(devices);
        Assert.Equal("ASUS Aura GPU", device.Name);
        await device.DisposeAsync();
    }

    [Fact]
    public async Task Probe_disposes_non_gpu_rgb_and_unknown_non_asus_buses_without_touching_i2c()
    {
        var amd = new FakeGpuI2cBus("a", new GpuPciIdentity(0x1002, 0x744C, GpuPciIdentity.AsusSubVendorId, 0x1234));
        var unknownMsi = new FakeGpuI2cBus("b", new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, 0x2C05, GpuPciIdentity.MsiSubVendorId, 0xFFFF));

        var devices = await NewProbe(new FakeGpuI2cBusFactory(amd, unknownMsi)).ProbeAsync([], CancellationToken.None);

        Assert.Empty(devices);
        Assert.True(amd.Disposed);
        Assert.True(unknownMsi.Disposed);
        Assert.Empty(amd.Calls);
        Assert.Empty(unknownMsi.Calls);
    }

    [Fact]
    public async Task Probe_delta_rescan_reuses_existing_device_and_disposes_the_reopened_bus()
    {
        var pci = new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, 0x2C05, GpuPciIdentity.AsusSubVendorId, 0x89F4);
        var firstBus = AsusBus(pci, "AUMA0-E6K5-1107", 4);
        var factory = new FakeGpuI2cBusFactory(firstBus);
        var probe = NewProbe(factory);
        var first = await probe.ProbeAsync([], CancellationToken.None);
        var existing = Assert.Single(first);

        var secondBus = AsusBus(pci, "AUMA0-E6K5-1107", 4);
        factory.Buses.Clear();
        factory.Buses.Add(secondBus);
        var second = await probe.ProbeAsync(first, CancellationToken.None);

        Assert.Same(existing, Assert.Single(second));
        Assert.True(secondBus.Disposed);
        Assert.Empty(secondBus.Calls);
        Assert.False(firstBus.Disposed);
        await existing.DisposeAsync();
    }

    [Fact]
    public async Task Probe_delta_rescan_reuses_existing_signature_fallback_device_without_i2c()
    {
        var unlisted = new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, 0x2C05, GpuPciIdentity.AsusSubVendorId, 0xFFFF);
        var factory = new FakeGpuI2cBusFactory(AsusBus(unlisted, "AUMA0-NEW", 8));
        var probe = NewProbe(factory);
        var first = await probe.ProbeAsync([], CancellationToken.None);

        var reopened = AsusBus(unlisted, "AUMA0-NEW", 8);
        factory.Buses.Clear();
        factory.Buses.Add(reopened);
        var second = await probe.ProbeAsync(first, CancellationToken.None);

        Assert.Same(Assert.Single(first), Assert.Single(second));
        Assert.True(reopened.Disposed);
        Assert.Empty(reopened.Calls);
        await second[0].DisposeAsync();
    }

    [Fact]
    public async Task Probe_falls_back_from_gigabyte_gen4_to_gen2_candidate_on_the_same_bus()
    {
        var pci = new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, 0x2F04, GpuPciIdentity.GigabyteSubVendorId, 0x4175);
        var bus = new FakeGpuI2cBus("NVAPI/GPU-0/I2C-1", pci);
        bus.FailWriteAddresses.Add(0x75); // Gen4 controller does not answer
        var factory = new FakeGpuI2cBusFactory(bus);

        var devices = await NewProbe(factory).ProbeAsync([], CancellationToken.None);

        var device = Assert.Single(devices);
        Assert.Equal("gigabyte-gpu-i2c:nvapi-gpu-0-i2c-1:71", device.Id);
        Assert.False(bus.Disposed);
        await device.DisposeAsync();
        Assert.True(bus.Disposed);
    }

    [Fact]
    public async Task Probe_disposes_the_bus_when_every_candidate_fails()
    {
        var pci = new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, 0x2F04, GpuPciIdentity.GigabyteSubVendorId, 0x4175);
        var bus = new FakeGpuI2cBus("b", pci);
        bus.FailWriteAddresses.Add(0x75);
        bus.FailWriteAddresses.Add(0x71);

        var devices = await NewProbe(new FakeGpuI2cBusFactory(bus)).ProbeAsync([], CancellationToken.None);

        Assert.Empty(devices);
        Assert.True(bus.Disposed);
    }

    [Fact]
    public async Task Probe_dry_run_opens_devices_without_state_changing_writes()
    {
        var pci = new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, 0x2C05, GpuPciIdentity.AsusSubVendorId, 0x89F4);
        var bus = AsusBus(pci, "AUMA0-E6K5-1107", 4);

        var devices = await NewProbe(new FakeGpuI2cBusFactory(bus), dryRun: true).ProbeAsync([], CancellationToken.None);

        var device = Assert.Single(devices);
        Assert.Empty(bus.StateChangingWrites);
        await device.DisposeAsync();
        Assert.Empty(bus.StateChangingWrites);
    }

    [Fact]
    public void Gpu_rgb_probe_is_registered_with_a_null_bus_factory_default()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddRigForgeCrossPlatformInfrastructure(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        Assert.Contains(services, d => d.ServiceType == typeof(IGpuI2cBusFactory) && d.ImplementationType == typeof(NullGpuI2cBusFactory));
        Assert.Contains(services, d => d.ServiceType == typeof(IDeviceProbe) && d.ImplementationType == typeof(GpuRgbProbe));
    }

    // ---------------------------------------------------------------- helpers

    private static GpuCatalogEntry Entry(ushort deviceId, ushort subVendorId, ushort subDeviceId) =>
        GpuPciCatalog.FindMatches(new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, deviceId, subVendorId, subDeviceId))[0];

    private static FakeGpuI2cBus AsusBus(GpuPciIdentity pci, string controllerName, int rawLedCount, string busId = "NVAPI/GPU-0/I2C-1", string adapterName = "NVIDIA GeForce RTX")
    {
        var bus = new FakeGpuI2cBus(busId, pci, adapterName);
        var bytes = System.Text.Encoding.ASCII.GetBytes(controllerName);
        Array.Copy(bytes, 0, bus.AuraRegs, 0x1000, Math.Min(bytes.Length, 16));
        bus.AuraRegs[0x1C03] = (byte)rawLedCount;
        return bus;
    }

    private static RgbColor[] Palette(int count, int seed)
    {
        var colors = new RgbColor[count];
        for (var i = 0; i < count; i++)
        {
            colors[i] = new RgbColor((byte)(seed + i * 3), (byte)(seed + i * 5 + 1), (byte)(seed + i * 7 + 2));
        }

        return colors;
    }

    private static async Task WarmAsync(IRgbDevice device, RgbColor[] a, RgbColor[] b)
    {
        for (var i = 0; i < 4; i++)
        {
            await device.UpdateLedsAsync(a, CancellationToken.None);
            await device.UpdateLedsAsync(b, CancellationToken.None);
        }
    }

    private static GpuRgbProbe NewProbe(IGpuI2cBusFactory factory, bool enabled = true, bool dryRun = false, IDeviceManifestCatalog? manifests = null) =>
        new(
            factory,
            Options.Create(new HardwareOptions { DryRun = dryRun, GpuRgb = new GpuRgbOptions { Enabled = enabled } }),
            NullLogger<GpuRgbProbe>.Instance,
            new RecordingTimer(),
            manifests);
}

internal sealed class NoopTimer : IPrecisionTimer
{
    public void Sleep(TimeSpan duration)
    {
    }
}

internal sealed class RecordingTimer : IPrecisionTimer
{
    public List<TimeSpan> Sleeps { get; } = [];

    public void Sleep(TimeSpan duration) => Sleeps.Add(duration);
}

internal sealed class FakeGpuI2cBusFactory(params FakeGpuI2cBus[] buses) : IGpuI2cBusFactory
{
    public List<FakeGpuI2cBus> Buses { get; } = [.. buses];

    public int OpenCount { get; private set; }

    public string BackendName => "Fake";

    public bool IsAvailable { get; set; } = true;

    public string DiagnosticStatus => "fake";

    public IReadOnlyList<IGpuI2cBus> OpenBuses()
    {
        OpenCount++;
        return [.. Buses];
    }
}

/// <summary>In-memory GPU I2C bus modelling the ENE pointer / data / read ports of the ASUS Aura controller.</summary>
internal sealed class FakeGpuI2cBus(string busId, GpuPciIdentity pciId, string adapterName = "NVIDIA GeForce RTX") : IGpuI2cBus
{
    private static readonly NoopLock SharedLock = new();
    private int _pointer;

    public sealed record Call(string Kind, byte Slave, byte[] Register, byte[] Data);

    public List<Call> Calls { get; } = [];

    public byte[] AuraRegs { get; } = new byte[0x10000];

    public HashSet<byte> FailWriteAddresses { get; } = [];

    public List<ushort> PointerTargets { get; } = [];

    public bool FailReads { get; set; }

    public byte[]? RawReadResponse { get; set; }

    public bool Record { get; set; } = true;

    public int WriteCount { get; private set; }

    public int LockCount { get; private set; }

    public int DisposeCount { get; private set; }

    public bool Disposed => DisposeCount > 0;

    public string BusId => busId;

    public string AdapterName => adapterName;

    public string Location => "NVAPI GPU 0 / I2C 1";

    public GpuPciIdentity PciId => pciId;

    public IEnumerable<Call> StateChangingWrites =>
        Calls.Where(c => (c.Kind == "W" && !IsPointerSelect(c)) || c.Kind == "RAW");

    public static bool IsPointerSelect(Call call) =>
        call.Kind == "W" && call.Register.Length == 1 && call.Register[0] == 0x00 && call.Data.Length == 2;

    public static string Describe(Call call) =>
        $"{call.Kind}:{Convert.ToHexString(call.Register)}={Convert.ToHexString(call.Data)}";

    public IDisposable AcquireBusLock()
    {
        LockCount++;
        return SharedLock;
    }

    public bool TryWriteRegisterBlock(byte slaveAddress, ReadOnlySpan<byte> registerAddress, ReadOnlySpan<byte> data)
    {
        WriteCount++;
        if (Record)
        {
            var call = new Call("W", slaveAddress, registerAddress.ToArray(), data.ToArray());
            Calls.Add(call);
            if (IsPointerSelect(call))
            {
                PointerTargets.Add((ushort)((data[0] << 8) | data[1]));
            }
        }

        if (FailWriteAddresses.Contains(slaveAddress))
        {
            return false;
        }

        if (registerAddress.Length == 1 && data.Length == 2 && registerAddress[0] == 0x00)
        {
            _pointer = (data[0] << 8) | data[1];
        }
        else if (registerAddress.Length == 1 && data.Length == 1 && registerAddress[0] == 0x01)
        {
            AuraRegs[_pointer] = data[0];
        }

        return true;
    }

    public bool TryReadRegisterBlock(byte slaveAddress, ReadOnlySpan<byte> registerAddress, Span<byte> destination)
    {
        if (Record)
        {
            Calls.Add(new Call("R", slaveAddress, registerAddress.ToArray(), []));
        }

        if (FailReads || registerAddress.Length != 1 || registerAddress[0] != 0x81 || destination.Length != 1)
        {
            return false;
        }

        destination[0] = AuraRegs[_pointer];
        return true;
    }

    public bool TryWriteRaw(byte slaveAddress, ReadOnlySpan<byte> payload)
    {
        WriteCount++;
        if (Record)
        {
            Calls.Add(new Call("RAW", slaveAddress, [], payload.ToArray()));
        }

        return !FailWriteAddresses.Contains(slaveAddress);
    }

    public bool TryReadRaw(byte slaveAddress, Span<byte> destination)
    {
        if (Record)
        {
            Calls.Add(new Call("RAWR", slaveAddress, [], []));
        }

        if (FailReads || RawReadResponse is null)
        {
            return false;
        }

        RawReadResponse.AsSpan(0, Math.Min(RawReadResponse.Length, destination.Length)).CopyTo(destination);
        return true;
    }

    public void Dispose() => DisposeCount++;

    private sealed class NoopLock : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
