using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Asus;
using RigForge.Infrastructure.CrossPlatform.Nzxt;

namespace RigForge.UnitTests.Protocols;

public sealed class AuraUsbProtocolTests
{
    [Fact]
    public void Direct_frame_splits_into_packets_of_twenty_and_flags_the_last_one()
    {
        var colors = Enumerable.Range(0, 25).Select(i => new RgbColor((byte)i, 1, 2)).ToArray();

        var packets = AuraUsbProtocol.DirectFrame(directChannel: 1, colors);

        Assert.Equal(2, packets.Count);
        Assert.All(packets, p => Assert.Equal(65, p.Length));
        Assert.Equal(new byte[] { 0xEC, 0x40, 0x01, 0, 20, 0, 1, 2 }, packets[0][..8]);
        Assert.Equal(new byte[] { 0xEC, 0x40, 0x81, 20, 5, 20, 1, 2 }, packets[1][..8]);
    }

    [Fact]
    public void Direct_frame_sets_the_overflow_flag_past_256_leds()
    {
        var packets = AuraUsbProtocol.DirectFrame(0x04, new RgbColor[300]);

        Assert.Equal(15, packets.Count);
        Assert.Equal(240, packets[12][3]);
        Assert.Equal(0x04 | 0x10, packets[13][2]);
        Assert.Equal(4, packets[13][3]);
        Assert.Equal(0x04 | 0x10 | 0x80, packets[14][2]);
    }

    [Theory]
    [InlineData("AULA3-AR32-0309", 3, 0)]
    [InlineData("AULA3-6K75-0219", 0, 1)]
    public void Config_table_maps_effect_channels_per_firmware_family(string firmware, int mainboardEffect, int firstArgbEffect)
    {
        var response = new byte[65];
        response[0] = 0xEC;
        response[1] = 0x30;
        response[4 + 0x02] = 3;
        response[4 + 0x1B] = 2;

        var channels = AuraUsbProtocol.ParseChannels(response, firmware, defaultArgbLeds: 24);

        Assert.Equal(4, channels.Count);
        Assert.Equal(new AuraChannel(AuraChannelKind.Mainboard, (byte)mainboardEffect, 0x04, 2), channels[0]);
        Assert.Equal(new AuraChannel(AuraChannelKind.Addressable, (byte)firstArgbEffect, 0, 24), channels[1]);
        Assert.Equal(2, channels[3].DirectChannel);
    }
}

public sealed class KrakenLcdTests
{
    [Theory]
    [InlineData(255, 255, 255, 0xFF, 0xFF)]
    [InlineData(255, 0, 0, 0xF8, 0x00)]
    [InlineData(0, 255, 0, 0x07, 0xE0)]
    [InlineData(0, 0, 255, 0x00, 0x1F)]
    public void Rgb565_is_big_endian(int r, int g, int b, int high, int low)
    {
        var destination = new byte[2];

        KrakenLcdUploader.ConvertToRgb565([(byte)r, (byte)g, (byte)b, 255], destination);

        Assert.Equal(new[] { (byte)high, (byte)low }, destination);
    }

    [Fact]
    public void Rgbx_clears_the_alpha_byte()
    {
        var destination = new byte[4];

        KrakenLcdUploader.ConvertToRgbx([10, 20, 30, 255], destination);

        Assert.Equal(new byte[] { 10, 20, 30, 0 }, destination);
    }

    [Fact]
    public void Bulk_header_carries_magic_mode_and_length()
    {
        var header = KrakenLcdUploader.BulkHeader(0x06, 115_200);

        Assert.Equal(20, header.Length);
        Assert.Equal(new byte[] { 0x12, 0xFA, 0x01, 0xE8 }, header[..4]);
        Assert.Equal(new byte[] { 0x06, 0, 0, 0, 0x00, 0xC2, 0x01, 0x00 }, header[12..]);
    }

    [Fact]
    public void Empty_bucket_memory_starts_at_zero()
    {
        var buckets = Enumerable.Range(0, 16).Select(_ => new byte[64]).ToArray();

        Assert.Equal(0, KrakenLcdUploader.BucketMemoryOffset(buckets, index: 0, sizeKb: 226));
    }

    [Fact]
    public void Bucket_large_enough_for_the_frame_is_reused()
    {
        var buckets = Enumerable.Range(0, 16).Select(_ => new byte[64]).ToArray();
        buckets[3][17] = 0x10;
        buckets[3][19] = 0xFF;

        Assert.Equal(0x10, KrakenLcdUploader.BucketMemoryOffset(buckets, index: 3, sizeKb: 226));
    }
}
