using RigForge.Core.Models;
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

    private static byte[] Report(string hex)
    {
        var report = new byte[64];
        Convert.FromHexString(hex).CopyTo(report, 0);
        return report;
    }
}
