using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform;
using RigForge.Infrastructure.CrossPlatform.Gpu;
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
}
