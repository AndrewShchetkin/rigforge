using Microsoft.Extensions.Options;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform;
using RigForge.Infrastructure.CrossPlatform.Hid;
using RigForge.Infrastructure.CrossPlatform.Manifests;
using RigForge.Infrastructure.CrossPlatform.Nzxt;

namespace RigForge.UnitTests.Protocols;

public sealed class NzxtProtocolTests
{
    // [0x67, 0x02] status report captured from an NZXT Control Hub (1E71:2022): fan on port 5, PWM, 611 RPM, all ports at 25 %.
    private static readonly byte[] ControlHubStatus = Report(
        "67026B69FC2CFA1D255A8534257005FF00000000020000000000000000000000630200000000000019191919190000001919191919");

    [Fact]
    public void Kraken_duty_profile_has_header_and_forty_duties()
    {
        var duties = Enumerable.Range(0, NzxtProtocol.ProfileLength).Select(i => (byte)i).ToArray();

        var report = NzxtProtocol.KrakenDutyProfile([0x01, 0x01, 0x00], duties);

        Assert.Equal(64, report.Length);
        Assert.Equal(new byte[] { 0x72, 0x01, 0x01, 0x00 }, report[..4]);
        Assert.Equal(duties, report[4..44]);
        Assert.All(report[44..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Fixed_duty_profile_keeps_a_firmware_safety_floor_for_hot_coolant()
    {
        var duties = NzxtProtocol.FixedDutyWithSafetyFloor(30, [(20, 0), (44, 0), (50, 70), (55, 100)]);

        Assert.Equal(30, duties[0]);
        Assert.Equal(30, duties[44 - 20]);
        Assert.Equal(70, duties[50 - 20]);
        Assert.All(duties[(55 - 20)..], d => Assert.Equal(100, d));
    }

    [Fact]
    public void Interpolated_profile_ends_at_full_speed()
    {
        var duties = NzxtProtocol.InterpolateProfile([(20, 50), (40, 90)], 20, 100);

        Assert.Equal(50, duties[0]);
        Assert.Equal(70, duties[30 - 20]);
        Assert.Equal(90, duties[45 - 20]);
        Assert.Equal(100, duties[^1]);
    }

    [Fact]
    public void Hue2_frame_sends_grb_colors_in_groups_of_twenty_then_applies()
    {
        var colors = Enumerable.Range(0, 25).Select(i => new RgbColor((byte)i, (byte)(100 + i), 200)).ToArray();

        var packets = NzxtProtocol.Hue2Frame(channel: 1, colors);

        Assert.Equal(3, packets.Count);
        Assert.Equal(new byte[] { 0x22, 0x10, 0x02, 0x00, 100, 0, 200 }, packets[0][..7]);
        Assert.Equal(new byte[] { 0x22, 0x11, 0x02, 0x00, 120, 20, 200 }, packets[1][..7]);
        Assert.Equal(new byte[] { 0x22, 0xA0, 0x02, 0x00, 0x01, 0x00, 0x00, 0x28 }, packets[2][..8]);
    }

    [Fact]
    public void Kraken_status_is_parsed()
    {
        var report = new byte[64];
        report[0] = 0x75;
        report[1] = 0x01;
        report[15] = 29;
        report[16] = 9;
        report[17] = 0x58;
        report[18] = 0x05;
        report[19] = 30;
        report[23] = 0x15;
        report[24] = 0x02;
        report[25] = 20;

        var status = NzxtProtocol.ParseKrakenStatus(report);

        Assert.NotNull(status);
        Approx.Equal(29.9f, status.LiquidTemperature);
        Assert.Equal(1368, status.PumpRpm);
        Assert.Equal(30, status.PumpDuty);
        Assert.Equal(533, status.FanRpm);
        Assert.Equal(20, status.FanDuty);
    }

    [Fact]
    public void Kraken_status_with_faulty_temperature_is_rejected()
    {
        var report = new byte[64];
        report[0] = 0x75;
        report[15] = 0xFF;
        report[16] = 0xFF;

        Assert.Null(NzxtProtocol.ParseKrakenStatus(report));
    }

    [Fact]
    public void Control_hub_status_reports_five_channels()
    {
        Assert.Equal(5, NzxtProtocol.FanChannelCount(ControlHubStatus, fallback: 3));

        var fans = NzxtProtocol.ParseSmartDeviceFans(ControlHubStatus, 5);

        Assert.Equal(5, fans.Count);
        Assert.Equal(new SmartDeviceFanStatus(4, Connected: true, Rpm: 611, Duty: 25), fans[4]);
        Assert.False(fans[0].Connected);
    }

    [Fact]
    public void Fan_channel_count_never_shrinks_the_catalog_value() =>
        Assert.Equal(3, NzxtProtocol.FanChannelCount(Report("6702000000000000000000000000020000"), fallback: 3));

    [Fact]
    public void Accessories_are_read_per_channel()
    {
        var report = new byte[64];
        report[0] = 0x21;
        report[1] = 0x03;
        report[14] = 3;
        report[15] = 0x14;
        report[21] = 0x14;
        report[22] = 0x13;

        var accessories = NzxtProtocol.ParseAccessories(report, maxChannels: 3);

        Assert.Equal(3, accessories.Count);
        Assert.Equal(new byte[] { 0x14 }, accessories[0]);
        Assert.Equal(new byte[] { 0x14, 0x13 }, accessories[1]);
        Assert.Empty(accessories[2]);
    }

    [Fact]
    public void Smart_device_duty_targets_one_channel()
    {
        var report = NzxtProtocol.SmartDeviceFixedDuty(channel: 4, duty: 55);

        Assert.Equal(new byte[] { 0x62, 0x01, 0x10, 0, 0, 0, 0, 55 }, report[..8]);
    }

    [Fact]
    public void Manifest_catalog_includes_built_ins_and_allows_json_directory_overrides()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"rigforge-manifests-{Guid.NewGuid():N}");
        var devicesDir = Path.Combine(tempDir, "devices");
        Directory.CreateDirectory(devicesDir);

        try
        {
            File.WriteAllText(
                Path.Combine(devicesDir, "custom.json"),
                """
                {
                  "id": "custom-control-hub",
                  "name": "Custom NZXT Control Hub",
                  "vendor": "NZXT",
                  "protocol": "NzxtHue2",
                  "vendorId": 7793,
                  "productId": 8226,
                  "fanChannels": 6,
                  "ledChannels": 6
                }
                """);

            var catalog = new JsonDeviceManifestCatalog(Options.Create(new StorageOptions { DataDirectory = tempDir }));

            Assert.Equal(9, catalog.ForProtocol(DeviceProtocolFamily.AsusAuraUsb).Count);
            Assert.Equal(5, catalog.ForProtocol(DeviceProtocolFamily.NzxtKraken).Count);
            Assert.Equal(18, catalog.ForProtocol(DeviceProtocolFamily.NzxtHue2).Count);

            var aura = catalog.FindUsb(0x0B05, 0x1867);
            Assert.NotNull(aura);
            Assert.Equal(0xFF72, aura.UsagePage);
            Assert.Equal(65, aura.MinOutputReportLength);

            var kraken = catalog.FindUsb(0x1E71, 0x300C);
            Assert.NotNull(kraken);
            Assert.NotNull(kraken.Display);
            Assert.Equal(640, kraken.Display.Width);
            Assert.True(kraken.Display.Experimental);

            var overridden = catalog.FindUsb(0x1E71, 0x2022);
            Assert.NotNull(overridden);
            Assert.Equal("Custom NZXT Control Hub", overridden.Name);
            Assert.Equal(6, overridden.FanChannels);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Normalize_serial_or_hash_preserves_valid_serial_and_hashes_placeholders()
    {
        Assert.Equal("ABC12345", HidDeviceExtensions.NormalizeSerialOrHash("  ABC12345  ", @"\\?\hid#vid_1e71&pid_2022#1"));

        var fromEmpty = HidDeviceExtensions.NormalizeSerialOrHash("", @"\\?\HID#VID_0B05&PID_19AF#7&1234");
        var fromAuraPlaceholder = HidDeviceExtensions.NormalizeSerialOrHash("AURA LED Controller", @"\\?\hid#vid_0b05&pid_19af#7&1234");
        var fromZeroPlaceholder = HidDeviceExtensions.NormalizeSerialOrHash("000000000000", @"\\?\hid#vid_0b05&pid_19af#7&1234");

        Assert.StartsWith("path-", fromEmpty, StringComparison.Ordinal);
        Assert.Equal(13, fromEmpty.Length);
        Assert.Equal(fromEmpty, fromAuraPlaceholder);
        Assert.Equal(fromEmpty, fromZeroPlaceholder);
    }

    [Fact]
    public void Hue2_maps_unknown_accessories_and_resizes_zones()
    {
        using var hid = new HidReportChannel(Stream.Null);
        var model = NzxtCatalog.Hue2Devices[0x2022];
        var device = new NzxtHue2Device(
            hid,
            model,
            id: "nzxt:2022:test",
            firmware: "1.0.0",
            accessories: [[0xFE], [0x13]],
            fanChannels: 0,
            fallbackDuty: 50,
            dryRun: true);

        Assert.Equal(2, device.Zones.Count);
        Assert.Equal("Channel 1: Accessory 0xFE", device.Zones[0].Name);
        Assert.Equal(12, device.Zones[0].LedCount);
        Assert.Equal(LedTopology.Linear, device.Zones[0].Topology);
        Assert.True(device.Zones[0].IsResizable);
        Assert.Equal(NzxtProtocol.MaxLedsPerChannel, device.Zones[0].MaxLedCount);

        Assert.True(device.TryResizeZone("led1", 50));
        Assert.Equal(NzxtProtocol.MaxLedsPerChannel, device.Zones[0].LedCount);

        Assert.True(device.TryResizeZone("led2", 10));
        Assert.Equal(10, device.Zones[1].LedCount);
        Assert.False(device.TryResizeZone("missing", 10));
    }

    private static byte[] Report(string hex)
    {
        var report = new byte[64];
        Convert.FromHexString(hex).CopyTo(report, 0);
        return report;
    }
}
