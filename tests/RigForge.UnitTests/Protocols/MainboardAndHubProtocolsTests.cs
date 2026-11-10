using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.AsRock;
using RigForge.Infrastructure.CrossPlatform.CoolerMaster;
using RigForge.Infrastructure.CrossPlatform.Corsair;
using RigForge.Infrastructure.CrossPlatform.Gigabyte;
using RigForge.Infrastructure.CrossPlatform.Hid;
using RigForge.Infrastructure.CrossPlatform.LianLi;
using RigForge.Infrastructure.CrossPlatform.Msi;
using RigForge.Infrastructure.CrossPlatform.Nzxt;

namespace RigForge.UnitTests.Protocols;

public sealed class MainboardAndHubProtocolsTests
{
    [Fact]
    public void Lighting_node_planar_packets_are_red_then_green_then_blue()
    {
        var colors = new[]
        {
            new RgbColor(1, 2, 3),
            new RgbColor(4, 5, 6),
            new RgbColor(7, 8, 9),
        };

        var red = Packet(CorsairProtocol.LightingNodeReportLength);
        var green = Packet(CorsairProtocol.LightingNodeReportLength);
        var blue = Packet(CorsairProtocol.LightingNodeReportLength);
        var commit = Packet(CorsairProtocol.LightingNodeReportLength);
        var software = Packet(CorsairProtocol.LightingNodeReportLength);
        var hardware = Packet(CorsairProtocol.LightingNodeReportLength);
        var begin = Packet(CorsairProtocol.LightingNodeReportLength);

        CorsairProtocol.WriteLightingNodeColorPacket(1, colors, 0, red);
        CorsairProtocol.WriteLightingNodeColorPacket(1, colors, 1, green);
        CorsairProtocol.WriteLightingNodeColorPacket(1, colors, 2, blue);
        CorsairProtocol.WriteLightingNodeCommit(commit);
        CorsairProtocol.WriteLightingNodeSoftwareMode(1, software);
        CorsairProtocol.WriteLightingNodeHardwareMode(1, hardware);
        CorsairProtocol.WriteLightingNodeBegin(1, begin);

        Assert.Equal(new byte[] { 0x00, 0x32, 0x01, 0x00, 0x03, 0x00, 0x01, 0x04, 0x07 }, red[..9]);
        Assert.Equal(new byte[] { 0x00, 0x32, 0x01, 0x00, 0x03, 0x01, 0x02, 0x05, 0x08 }, green[..9]);
        Assert.Equal(new byte[] { 0x00, 0x32, 0x01, 0x00, 0x03, 0x02, 0x03, 0x06, 0x09 }, blue[..9]);
        Assert.Equal(new byte[] { 0x00, 0x33, 0xFF }, commit[..3]);
        Assert.Equal(new byte[] { 0x00, 0x38, 0x01, 0x02 }, software[..4]);
        Assert.Equal(new byte[] { 0x00, 0x38, 0x01, 0x01 }, hardware[..4]);
        Assert.Equal(new byte[] { 0x00, 0x34, 0x01 }, begin[..3]);
        Assert.All(red[9..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Lighting_node_second_chunk_starts_at_led_50()
    {
        var colors = new RgbColor[51];
        colors[50] = new RgbColor(10, 20, 30);

        var packet = Packet(CorsairProtocol.LightingNodeReportLength);
        CorsairProtocol.WriteLightingNodeColorPacket(0, colors, 3, packet);

        Assert.Equal(6, CorsairProtocol.LightingNodeColorPacketCount(51));
        Assert.Equal(new byte[] { 0x00, 0x32, 0x00, 50, 0x01, 0x00, 10 }, packet[..7]);
    }

    [Fact]
    public void Commander_core_rgb_stream_chunks_on_triplet_boundaries()
    {
        var wake = Packet(97);
        var open = Packet(97);
        var close = Packet(97);
        var hardware = Packet(97);
        CorsairProtocol.WriteCommanderCoreSoftwareMode(wake);
        CorsairProtocol.WriteCommanderCoreOpenEndpoint(open);
        CorsairProtocol.WriteCommanderCoreCloseEndpoint(close);
        CorsairProtocol.WriteCommanderCoreHardwareMode(hardware);

        Assert.Equal(new byte[] { 0x00, 0x08, 0x01, 0x03, 0x00, 0x02 }, wake[..6]);
        Assert.Equal(new byte[] { 0x00, 0x08, 0x0D, 0x00, 0x22, 0x00 }, open[..6]);
        Assert.Equal(new byte[] { 0x00, 0x08, 0x05, 0x01 }, close[..4]);
        Assert.Equal(new byte[] { 0x00, 0x08, 0x01, 0x03, 0x00, 0x01 }, hardware[..6]);

        var two = new[] { new RgbColor(1, 2, 3), new RgbColor(4, 5, 6) };
        var first = Packet(97);
        CorsairProtocol.WriteCommanderCoreRgbPacket(two, 0, 97, first);
        Assert.Equal(new byte[] { 0x00, 0x08, 0x06, 0x00, 0x08, 0x00, 0x00, 0x00, 0x12, 0x00, 1, 2, 3, 4, 5, 6 }, first[..16]);

        var many = new RgbColor[30];
        many[29] = new RgbColor(9, 8, 7);
        Assert.Equal(2, CorsairProtocol.CommanderCoreRgbPacketCount(30, 97));
        var head = Packet(97);
        var tail = Packet(97);
        CorsairProtocol.WriteCommanderCoreRgbPacket(many, 0, 97, head);
        CorsairProtocol.WriteCommanderCoreRgbPacket(many, 1, 97, tail);
        Assert.Equal(new byte[] { 0x00, 0x08, 0x06, 0x00, 92, 0x00 }, head[..6]);
        Assert.Equal(0x12, head[8]);
        Assert.Equal(new byte[] { 0x00, 0x08, 0x07, 0x00, 9, 8, 7 }, tail[..7]);

        var wide = Packet(1025);
        var wideColors = new RgbColor[144];
        wideColors[0] = new RgbColor(1, 2, 3);
        CorsairProtocol.WriteCommanderCoreRgbPacket(wideColors, 0, 1025, wide);
        Assert.Equal(1, CorsairProtocol.CommanderCoreRgbPacketCount(144, 1025));
        Assert.Equal(0x06, wide[2]);
        Assert.Equal((byte)((144 * 3 + 2) & 0xFF), wide[4]);
        Assert.Equal((byte)((144 * 3 + 2) >> 8), wide[5]);
    }

    [Fact]
    public void Mystic_light_185_report_uses_direct_zones_and_never_saves()
    {
        var colors = new[]
        {
            new RgbColor(1, 2, 3),
            new RgbColor(4, 5, 6),
            new RgbColor(7, 8, 9),
            new RgbColor(10, 11, 12),
            new RgbColor(13, 14, 15),
            new RgbColor(16, 17, 18),
        };
        var report = Packet(MsiMysticLightProtocol.ReportLength);
        MsiMysticLightProtocol.Write185Report(colors, report);

        Assert.Equal(185, report.Length);
        Assert.Equal(0x52, report[0]);
        Assert.Equal(0, report[184]);
        AssertZone(report, MsiMysticLightProtocol.Jrgb1Offset, new RgbColor(4, 5, 6));
        AssertZone(report, MsiMysticLightProtocol.Jrgb2Offset, new RgbColor(7, 8, 9));
        AssertZone(report, MsiMysticLightProtocol.Jrainbow1Offset, new RgbColor(10, 11, 12));
        AssertZone(report, MsiMysticLightProtocol.Jrainbow2Offset, new RgbColor(13, 14, 15));
        AssertZone(report, MsiMysticLightProtocol.OnboardOffset, new RgbColor(1, 2, 3));
        AssertZone(report, MsiMysticLightProtocol.OnboardOffset + 10, new RgbColor(16, 17, 18));
        Assert.Equal(0, report[MsiMysticLightProtocol.Jpipe1Offset]);
        Assert.Equal(0, report[MsiMysticLightProtocol.Jpipe2Offset]);

        var saturated = Packet(MsiMysticLightProtocol.ReportLength);
        MsiMysticLightProtocol.Write185Report(Enumerable.Repeat(new RgbColor(255, 255, 255), 5).ToArray(), saturated);
        Assert.Equal(0, saturated[184]);
        Assert.Equal(0x01, saturated[1]);
    }

    [Fact]
    public void Mystic_light_argb_stream_keeps_the_save_byte_clear()
    {
        var colors = new[] { new RgbColor(1, 2, 3), new RgbColor(4, 5, 6) };
        var packet = Packet(64);
        MsiMysticLightProtocol.WriteArgbStream(MsiMysticLightProtocol.ZoneJrainbow1, 0, colors, packet);

        Assert.Equal(new byte[] { 0x52, 0x05, 0x00, 0x00, 0x02, 0x01, 0x00, 0x00, 1, 2, 3, 4, 5, 6 }, packet[..14]);
        Assert.Equal(0, packet[6]);
    }

    [Fact]
    public void Fusion2_addressable_packets_are_grb_and_execute_is_volatile()
    {
        var colors = new RgbColor[20];
        colors[0] = new RgbColor(1, 2, 3);
        colors[19] = new RgbColor(4, 5, 6);
        var first = Packet(64);
        var second = Packet(64);
        var onboard = Packet(64);
        var execute = Packet(64);
        GigabyteFusion2Protocol.WriteAddressablePacket(GigabyteFusion2Protocol.DLed1, colors, 0, first);
        GigabyteFusion2Protocol.WriteAddressablePacket(GigabyteFusion2Protocol.DLed2, colors, 1, second);
        GigabyteFusion2Protocol.WriteOnboardPacket(GigabyteFusion2Protocol.OnboardHeader, new RgbColor(9, 8, 7), onboard);
        GigabyteFusion2Protocol.WriteExecute(execute);

        Assert.Equal(new byte[] { 0xCC, 0x58, 0x00, 0x00, 57, 2, 1, 3 }, first[..8]);
        Assert.Equal(new byte[] { 0xCC, 0x59, 57, 0x00, 3, 5, 4, 6 }, second[..8]);
        Assert.Equal(0xCC, onboard[0]);
        Assert.Equal(0x20, onboard[1]);
        Assert.Equal(0x01, onboard[0x0B]);
        Assert.Equal(0x5A, onboard[0x0C]);
        Assert.Equal(new byte[] { 9, 8, 7 }, onboard[0x0E..0x11]);
        Assert.Equal(new byte[] { 0xCC, 0x28, 0xFF }, execute[..3]);
        Assert.NotEqual(0x29, execute[1]);
        Assert.NotEqual(0x30, execute[1]);
        Assert.All(new[] { first, second, onboard, execute }, packet =>
        {
            Assert.NotEqual(0x29, packet[1]);
            Assert.NotEqual(0x30, packet[1]);
        });
    }

    [Fact]
    public void Polychrome_addressable_frames_carry_a_color_payload()
    {
        var onboard = Packet(65);
        AsRockPolychromeProtocol.WriteOnboardPacket(AsRockPolychromeProtocol.OnboardChannel, new RgbColor(9, 8, 7), onboard);
        Assert.Equal(new byte[] { 0x00, 0x10, 0x00, 0x00, 0x01, 9, 8, 7 }, onboard[..8]);

        var colors = new RgbColor[21];
        colors[20] = new RgbColor(1, 2, 3);
        var tail = Packet(65);
        AsRockPolychromeProtocol.WriteAddressablePacket(AsRockPolychromeProtocol.ArgbChannel, 1, colors, tail);
        Assert.Equal(2, AsRockPolychromeProtocol.PacketCount(21));
        Assert.Equal(new byte[] { 0x00, 0x12, 0x01, 20, 0x01, 1, 2, 3 }, tail[..8]);
        Assert.True(tail[4] > 0);
    }

    [Fact]
    public void Uni_hub_mode_header_and_rgb_or_rbg_frames()
    {
        var mode = Packet(65);
        LianLiUniHubProtocol.WriteModeHeader(2, 48, mode);
        Assert.Equal(new byte[] { 0xE0, 0x10, 0x60, 0x02, 48 }, mode[..5]);

        var colors = new[] { new RgbColor(0x11, 0x22, 0x33) };
        var rgb = Packet(65);
        var rbg = Packet(65);
        LianLiUniHubProtocol.WriteColorPacket(2, 0, colors, LianLiColorOrder.Rgb, 65, rgb);
        LianLiUniHubProtocol.WriteColorPacket(2, 0, colors, LianLiColorOrder.Rbg, 65, rbg);
        Assert.Equal(new byte[] { 0xE0, 0x32, 0x00, 0x01, 0x11, 0x22, 0x33 }, rgb[..7]);
        Assert.Equal(new byte[] { 0xE0, 0x32, 0x00, 0x01, 0x11, 0x33, 0x22 }, rbg[..7]);

        var apply = Packet(65);
        LianLiUniHubProtocol.WriteStaticApply(2, apply);
        Assert.Equal(new byte[] { 0xE0, 0x12, 0x01, 0x00, 0x00, 0x00 }, apply[..6]);
        Assert.Equal(LianLiColorOrder.Rbg, LianLiUniHubProtocol.ColorOrder(LianLiUniHubProtocol.UniHubSlInfinity));
        Assert.Equal(120, LianLiUniHubProtocol.MaxLedsPerChannel(LianLiUniHubProtocol.UniHubSlV2));
        Assert.Equal(64, LianLiUniHubProtocol.MaxLedsPerChannel(LianLiUniHubProtocol.UniHubSl));
    }

    [Fact]
    public void Cooler_master_direct_mode_and_led_chunks()
    {
        var mode = Packet(65);
        CoolerMasterArgbProtocol.WriteDirectMode(2, mode);
        Assert.Equal(new byte[] { 0x00, 0x80, 0x10, 0x01, 0x02, 0x00 }, mode[..6]);

        var one = Packet(65);
        CoolerMasterArgbProtocol.WriteColorPacket(0, 0, [new RgbColor(1, 2, 3)], one);
        Assert.Equal(new byte[] { 0x00, 0x80, 0x10, 0x02, 0x00, 0x01, 1, 2, 3 }, one[..9]);

        var colors = new RgbColor[20];
        colors[19] = new RgbColor(4, 5, 6);
        var tail = Packet(65);
        CoolerMasterArgbProtocol.WriteColorPacket(1, 1, colors, tail);
        Assert.Equal(new byte[] { 0x00, 0x81, 0x10, 0x02, 0x01, 0x01, 4, 5, 6 }, tail[..9]);
    }

    [Fact]
    public void Kraken_x3_rgb_uses_channel_ids_and_the_pump_keeps_a_safety_floor()
    {
        var colors = new RgbColor[25];
        colors[0] = new RgbColor(1, 2, 3);
        colors[20] = new RgbColor(4, 5, 6);
        var first = Packet(64);
        var second = Packet(64);
        var apply = Packet(64);
        NzxtProtocol.WriteKrakenX3RgbPacket(NzxtProtocol.KrakenX3RingChannel, 0, colors, first);
        NzxtProtocol.WriteKrakenX3RgbPacket(NzxtProtocol.KrakenX3RingChannel, 1, colors, second);
        NzxtProtocol.WriteKrakenX3RgbPacket(NzxtProtocol.KrakenX3LogoChannel, 1, [new RgbColor(1, 2, 3)], apply);

        Assert.Equal(3, NzxtProtocol.KrakenX3PacketCount(25));
        Assert.Equal(new byte[] { 0x22, 0x10, 0x02, 0x00, 2, 1, 3 }, first[..7]);
        Assert.Equal(new byte[] { 0x22, 0x11, 0x02, 0x00, 5, 4, 6 }, second[..7]);
        Assert.Equal(new byte[] { 0x22, 0xA0, 0x04, 0x00, 0x01, 0x00, 0x00, 0x08, 0x00, 0x00, 0x80, 0x00, 0x32, 0x00, 0x00, 0x01 }, apply[..16]);

        var duties = NzxtProtocol.FixedDutyWithSafetyFloor(10, [(20, 0), (44, 0), (50, 70), (55, 100)]);
        var profile = NzxtProtocol.KrakenDutyProfile([0x01, 0x00, 0x00], duties);
        Assert.Equal(new byte[] { 0x72, 0x01, 0x00, 0x00 }, profile[..4]);
        Assert.Equal(10, profile[4]);
        Assert.Equal(70, profile[4 + (50 - 20)]);
        Assert.Equal(100, profile[4 + (59 - 20)]);
    }

    [Fact]
    public void Packet_writers_allocate_nothing_after_warmup()
    {
        var nodeColors = new RgbColor[51];
        var coreColors = new RgbColor[30];
        var wideColors = new RgbColor[144];
        var mystic = new RgbColor[6];
        var stream = new RgbColor[20];
        var fusion = new RgbColor[20];
        var poly = new RgbColor[21];
        var lian = new RgbColor[48];
        var cm = new RgbColor[20];
        var kraken = new RgbColor[25];
        var node = Packet(65);
        var core = Packet(97);
        var wide = Packet(1025);
        var report185 = Packet(185);
        var report64 = Packet(64);
        var fusionPacket = Packet(64);
        var polyPacket = Packet(65);
        var lianPacket = Packet(65);
        var cmPacket = Packet(65);

        void WriteAll()
        {
            CorsairProtocol.WriteLightingNodeSoftwareMode(0, node);
            CorsairProtocol.WriteLightingNodeHardwareMode(1, node);
            CorsairProtocol.WriteLightingNodeBegin(0, node);
            CorsairProtocol.WriteLightingNodeCommit(node);
            for (var i = 0; i < CorsairProtocol.LightingNodeColorPacketCount(nodeColors.Length); i++)
            {
                CorsairProtocol.WriteLightingNodeColorPacket(0, nodeColors, i, node);
            }

            CorsairProtocol.WriteCommanderCoreSoftwareMode(core);
            CorsairProtocol.WriteCommanderCoreHardwareMode(core);
            CorsairProtocol.WriteCommanderCoreOpenEndpoint(core);
            CorsairProtocol.WriteCommanderCoreCloseEndpoint(core);
            for (var i = 0; i < CorsairProtocol.CommanderCoreRgbPacketCount(coreColors.Length, 97); i++)
            {
                CorsairProtocol.WriteCommanderCoreRgbPacket(coreColors, i, 97, core);
            }

            CorsairProtocol.WriteCommanderCoreRgbPacket(wideColors, 0, 1025, wide);
            MsiMysticLightProtocol.Write185Report(mystic, report185);
            for (var i = 0; i < MsiMysticLightProtocol.StreamPacketCount(stream.Length); i++)
            {
                MsiMysticLightProtocol.WriteArgbStream(MsiMysticLightProtocol.ZoneJrainbow1, i, stream, report64);
            }

            for (var i = 0; i < GigabyteFusion2Protocol.PacketCount(fusion.Length); i++)
            {
                GigabyteFusion2Protocol.WriteAddressablePacket(GigabyteFusion2Protocol.DLed1, fusion, i, fusionPacket);
            }

            GigabyteFusion2Protocol.WriteOnboardPacket(GigabyteFusion2Protocol.OnboardHeader, default, fusionPacket);
            GigabyteFusion2Protocol.WriteExecute(fusionPacket);
            AsRockPolychromeProtocol.WriteOnboardPacket(0, default, polyPacket);
            for (var i = 0; i < AsRockPolychromeProtocol.PacketCount(poly.Length); i++)
            {
                AsRockPolychromeProtocol.WriteAddressablePacket(1, i, poly, polyPacket);
            }

            LianLiUniHubProtocol.WriteModeHeader(0, 48, lianPacket);
            LianLiUniHubProtocol.WriteStaticApply(0, lianPacket);
            for (var i = 0; i < LianLiUniHubProtocol.ColorPacketCount(lian.Length, 65); i++)
            {
                LianLiUniHubProtocol.WriteColorPacket(0, i, lian, LianLiColorOrder.Rgb, 65, lianPacket);
                LianLiUniHubProtocol.WriteColorPacket(1, i, lian, LianLiColorOrder.Rbg, 65, lianPacket);
            }

            CoolerMasterArgbProtocol.WriteDirectMode(0, cmPacket);
            for (var i = 0; i < CoolerMasterArgbProtocol.PacketCount(cm.Length); i++)
            {
                CoolerMasterArgbProtocol.WriteColorPacket(0, i, cm, cmPacket);
            }

            for (var i = 0; i < NzxtProtocol.KrakenX3PacketCount(kraken.Length); i++)
            {
                NzxtProtocol.WriteKrakenX3RgbPacket(NzxtProtocol.KrakenX3RingChannel, i, kraken, report64);
            }
        }

        for (var warmup = 0; warmup < 50; warmup++)
        {
            WriteAll();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < 100; iteration++)
        {
            WriteAll();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public async Task Resizable_zones_honor_their_maximum_and_dry_run_writes_nothing()
    {
        var nodeHid = new HidReportChannel(Stream.Null);
        await using var node = new CorsairLightingNodeDevice(nodeHid, "node", "Node", "path", 0x0C0B, 2, 24, dryRun: true);
        Assert.Equal(2, node.Zones.Count);
        Assert.Equal(24, node.Zones[0].LedCount);
        Assert.True(node.TryResizeZone("channel1", 120));
        Assert.Equal(120, node.Zones[0].LedCount);
        Assert.False(node.TryResizeZone("channel1", 121));
        Assert.False(node.TryResizeZone("missing", 10));
        await node.UpdateLedsAsync(new[] { new RgbColor(1, 2, 3) }, CancellationToken.None);

        var coreHid = new HidReportChannel(Stream.Null, 97);
        await using var core = new CorsairCommanderCoreDevice(coreHid, "core", "Core", "path", 0x0C1C, 97, 24, dryRun: true);
        Assert.True(core.TryResizeZone("rgb", 144));
        Assert.False(core.TryResizeZone("rgb", 145));

        var msiHid = new HidReportChannel(Stream.Null, 64);
        await using var msi = new MsiMysticLightDevice(msiHid, "msi", "MSI", "path", 0x7E16, MsiReportKind.ArgbStream, dryRun: true);
        Assert.True(msi.TryResizeZone("jrainbow1", 120));
        Assert.False(msi.TryResizeZone("jrainbow1", 121));
        Assert.False(msi.TryResizeZone("onboard", 2));

        var fusionHid = new HidReportChannel(Stream.Null, 64);
        await using var fusion = new GigabyteFusion2Device(fusionHid, "gb", "Gigabyte", "path", 0x8297, 24, dryRun: true);
        Assert.False(fusion.TryResizeZone("onboard", 2));
        Assert.True(fusion.TryResizeZone("dled2", 120));
        Assert.False(fusion.TryResizeZone("dled2", 121));

        var asrockHid = new HidReportChannel(Stream.Null, 65);
        await using var asrock = new AsRockPolychromeDevice(asrockHid, "asr", "ASRock", "path", 0x01A2, 24, dryRun: true);
        Assert.Equal(LedTopology.Linear, asrock.Zones[1].Topology);
        Assert.True(asrock.TryResizeZone("argb", 100));
        Assert.False(asrock.TryResizeZone("argb", 101));

        var lianHid = new HidReportChannel(Stream.Null, 65);
        await using var lian = new LianLiUniHubDevice(lianHid, "ll", "UNI", "path", 0xA100, LianLiColorOrder.Rgb, 65, 48, 64, dryRun: true);
        Assert.Equal(4, lian.Zones.Count);
        Assert.Equal(48, lian.Zones[0].LedCount);
        Assert.True(lian.TryResizeZone("channel4", 64));
        Assert.False(lian.TryResizeZone("channel4", 65));

        var cmHid = new HidReportChannel(Stream.Null, 65);
        await using var cm = new CoolerMasterArgbDevice(cmHid, "cm", "CM", "path", 0x0173, 24, dryRun: true);
        Assert.Equal(3, cm.Zones.Count);
        Assert.True(cm.TryResizeZone("port3", 80));
        Assert.False(cm.TryResizeZone("port3", 81));
    }

    [Fact]
    public async Task Direct_frames_keep_save_bytes_clear_on_the_wire()
    {
        var msiCapture = new CaptureStream();
        var msiHid = new HidReportChannel(msiCapture, 185, 64);
        await using (var msi = new MsiMysticLightDevice(msiHid, "msi", "MSI", "path", 0x7E16, MsiReportKind.Zones185, dryRun: false))
        {
            await msi.UpdateLedsAsync(Enumerable.Repeat(new RgbColor(255, 0, 0), 5).ToArray(), CancellationToken.None);
        }

        Assert.Equal(0x52, msiCapture.Writes[0][0]);
        Assert.Equal(0, msiCapture.Writes[0][184]);

        var fusionCapture = new CaptureStream();
        var fusionHid = new HidReportChannel(fusionCapture, 64, 64);
        await using (var fusion = new GigabyteFusion2Device(fusionHid, "gb", "Gigabyte", "path", 0x8297, 1, dryRun: false))
        {
            await fusion.UpdateLedsAsync(
                new[] { new RgbColor(1, 2, 3), new RgbColor(4, 5, 6), new RgbColor(7, 8, 9) },
                CancellationToken.None);
        }

        Assert.Contains(fusionCapture.Writes, packet => packet[1] == 0x58);
        Assert.Equal(new byte[] { 0xCC, 0x28, 0xFF }, fusionCapture.Writes[^1][..3]);
        Assert.DoesNotContain(fusionCapture.Writes, packet => packet[1] is 0x29 or 0x30);

        var nodeCapture = new CaptureStream();
        var nodeHid = new HidReportChannel(nodeCapture, 65, 64);
        var node = new CorsairLightingNodeDevice(nodeHid, "node", "Node", "path", 0x0C1A, 1, 1, dryRun: false);
        await node.UpdateLedsAsync(new[] { new RgbColor(1, 0, 0) }, CancellationToken.None);
        await node.DisposeAsync();
        Assert.Contains(nodeCapture.Writes, packet => packet[1] == 0x33 && packet[2] == 0xFF);
        Assert.Contains(nodeCapture.Writes, packet => packet[1] == 0x38 && packet[3] == 0x01);
    }

    [Fact]
    public async Task Kraken_x3_exposes_ring_and_logo_and_clamps_the_pump()
    {
        var plain = new HidReportChannel(Stream.Null);
        await using var nonRing = new NzxtKrakenDevice(plain, NzxtCatalog.Krakens[0x300E], "nzxt:300e", "1.0.0", 0, null, dryRun: true);
        Assert.Equal(DeviceCapabilities.FanControl | DeviceCapabilities.Sensors, nonRing.Capabilities);
        Assert.Empty(nonRing.Zones);

        var eliteHid = new HidReportChannel(Stream.Null);
        await using var elite = new NzxtKrakenDevice(eliteHid, NzxtCatalog.Krakens[0x3012], "nzxt:3012", "1.2.0", 0, null, dryRun: true);
        Assert.Equal(DeviceCapabilities.FanControl | DeviceCapabilities.Sensors | DeviceCapabilities.Rgb, elite.Capabilities);
        Assert.Equal("ring", elite.Zones[0].Id);
        Assert.Equal(LedTopology.Ring, elite.Zones[0].Topology);
        Assert.Equal(24, elite.Zones[0].LedCount);

        var x3 = new HidReportChannel(Stream.Null);
        await using var kraken = new NzxtKrakenX3Device(x3, NzxtCatalog.KrakenX3Devices[0x2007], "nzxt:2007:test", "1.0.0", dryRun: true);
        Assert.Equal(DeviceCapabilities.Rgb | DeviceCapabilities.FanControl | DeviceCapabilities.Sensors, kraken.Capabilities);
        Assert.Equal(LedTopology.Ring, kraken.Zones[0].Topology);
        Assert.Equal(8, kraken.Zones[0].LedCount);
        Assert.Equal("logo", kraken.Zones[1].Id);
        Assert.Equal(LedTopology.Point, kraken.Zones[1].Topology);
        Assert.Equal(1, kraken.Zones[1].LedCount);
        Assert.False(kraken.TryResizeZone("ring", 12));
        Assert.Equal(20, kraken.Channels[0].MinDutyPercent);

        var capture = new CaptureStream();
        var dutyHid = new HidReportChannel(capture, 64, 64);
        await using var driven = new NzxtKrakenX3Device(dutyHid, NzxtCatalog.KrakenX3Devices[0x2014], "nzxt:2014", "1.0.0", dryRun: false);
        await driven.SetDutyAsync("pump", 5, CancellationToken.None);
        var profile = capture.Writes[^1];
        Assert.Equal(new byte[] { 0x72, 0x01, 0x00, 0x00 }, profile[..4]);
        Assert.Equal(20, profile[4]);
        Assert.Equal(70, profile[4 + (50 - 20)]);
        Assert.Equal(100, profile[4 + (59 - 20)]);

        var frame = new RgbColor[9];
        frame[0] = new RgbColor(1, 2, 3);
        frame[8] = new RgbColor(4, 5, 6);
        await driven.UpdateLedsAsync(frame, CancellationToken.None);
        Assert.Contains(capture.Writes, packet => packet[0] == 0x22 && packet[2] == NzxtProtocol.KrakenX3RingChannel);
        Assert.Contains(capture.Writes, packet => packet[0] == 0x22 && packet[2] == NzxtProtocol.KrakenX3LogoChannel);
    }

    private static void AssertZone(byte[] report, int offset, RgbColor color)
    {
        Assert.Equal(0x01, report[offset]);
        Assert.Equal(color.R, report[offset + 1]);
        Assert.Equal(color.G, report[offset + 2]);
        Assert.Equal(color.B, report[offset + 3]);
        Assert.Equal(0x28, report[offset + 4]);
        Assert.Equal(0x80, report[offset + 8]);
    }

    private static byte[] Packet(int length) => new byte[length];

    private sealed class CaptureStream : Stream
    {
        public List<byte[]> Writes { get; } = [];

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => 0;

        public override long Position { get => 0; set { } }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override long Seek(long offset, SeekOrigin origin) => 0;

        public override void SetLength(long value)
        {
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            var copy = new byte[count];
            Buffer.BlockCopy(buffer, offset, copy, 0, count);
            Writes.Add(copy);
        }
    }
}
