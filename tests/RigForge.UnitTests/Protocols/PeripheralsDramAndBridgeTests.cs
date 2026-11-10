using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform;
using RigForge.Infrastructure.CrossPlatform.Discovery;
using RigForge.Infrastructure.CrossPlatform.Dram;
using RigForge.Infrastructure.CrossPlatform.OpenRgb;
using RigForge.Infrastructure.CrossPlatform.Peripherals.Logitech;
using RigForge.Infrastructure.CrossPlatform.Peripherals.QmkVial;
using RigForge.Infrastructure.CrossPlatform.Peripherals.Razer;
using RigForge.Infrastructure.CrossPlatform.Smbus;

namespace RigForge.UnitTests.Protocols;

public sealed class PeripheralsDramAndBridgeTests
{
    [Fact]
    public void Qmk_hsv_and_direct_packet_match_the_33_byte_vialrgb_layout()
    {
        QmkVialProtocol.ToQmkHsv(new RgbColor(255, 0, 0), out var redH, out var redS, out var redV);
        QmkVialProtocol.ToQmkHsv(new RgbColor(0, 255, 0), out var greenH, out var greenS, out var greenV);
        QmkVialProtocol.ToQmkHsv(new RgbColor(0, 0, 255), out var blueH, out _, out _);
        QmkVialProtocol.ToQmkHsv(RgbColor.White, out var whiteH, out var whiteS, out var whiteV);
        QmkVialProtocol.ToQmkHsv(RgbColor.Black, out var blackH, out var blackS, out var blackV);

        Assert.Equal((byte)0, redH);
        Assert.Equal((byte)255, redS);
        Assert.Equal((byte)255, redV);
        Assert.Equal((byte)85, greenH);
        Assert.Equal((byte)255, greenS);
        Assert.Equal((byte)255, greenV);
        Assert.Equal((byte)171, blueH);
        Assert.Equal((byte)0, whiteH);
        Assert.Equal((byte)0, whiteS);
        Assert.Equal((byte)255, whiteV);
        Assert.Equal((byte)0, blackH);
        Assert.Equal((byte)0, blackS);
        Assert.Equal((byte)0, blackV);

        var packet = new byte[QmkVialProtocol.ReportLength];
        RgbColor[] colors = [new RgbColor(255, 0, 0), new RgbColor(0, 255, 0)];
        QmkVialProtocol.WriteDirectPacket(0x0102, colors, packet);

        Assert.Equal(33, packet.Length);
        Assert.Equal(QmkVialProtocol.ReportId, packet[0]);
        Assert.Equal(QmkVialProtocol.LightingSetValue, packet[1]);
        Assert.Equal(QmkVialProtocol.VialRgbDirectFastSet, packet[2]);
        Assert.NotEqual(QmkVialProtocol.LightingSave, packet[1]);
        Assert.Equal(0x02, packet[3]);
        Assert.Equal(0x01, packet[4]);
        Assert.Equal(2, packet[5]);
        Assert.Equal(redH, packet[6]);
        Assert.Equal(redS, packet[7]);
        Assert.Equal(redV, packet[8]);
        Assert.Equal(greenH, packet[9]);
        Assert.Equal(greenS, packet[10]);
        Assert.Equal(greenV, packet[11]);

        QmkVialProtocol.WriteDirectMode(200, packet);
        Assert.Equal(QmkVialProtocol.LightingSetValue, packet[1]);
        Assert.Equal(QmkVialProtocol.VialRgbSetMode, packet[2]);
        Assert.Equal(0x01, packet[3]);
        Assert.Equal(0x00, packet[4]);
        Assert.Equal(200, packet[8]);
        Assert.NotEqual(QmkVialProtocol.LightingSave, packet[1]);

        var corner = QmkVialProtocol.ToLedPosition(224, 64);
        var origin = QmkVialProtocol.ToLedPosition(0, 0);
        Assert.Equal(1f, corner.X);
        Assert.Equal(1f, corner.Y);
        Assert.Equal(0f, origin.X);
        Assert.Equal(0f, origin.Y);

        Assert.True(QmkVialProtocol.IsViaProtocolResponse([0x00, 0x01, 0x00, 0x0C]));
        Assert.True(QmkVialProtocol.TryParseVialRgbInfo([0x00, 0x08, 0x40, 0x01, 0x00, 0xFF], out var version, out var maxBrightness));
        Assert.Equal((ushort)1, version);
        Assert.Equal((byte)255, maxBrightness);
        Assert.True(QmkVialProtocol.TryParseLedCount([0x00, 0x08, 0x43, 33, 0x00], out var ledCount));
        Assert.Equal(33, ledCount);
        Assert.True(QmkVialProtocol.TryParseLedInfo([0x00, 0x08, 0x44, 112, 32, 0x01], out var x, out var y, out var flags));
        Assert.Equal((byte)112, x);
        Assert.Equal((byte)32, y);
        Assert.Equal((byte)1, flags);
        Assert.Equal(0.5f, QmkVialProtocol.ToLedPosition(x, y).X);
        Assert.Equal(0.5f, QmkVialProtocol.ToLedPosition(x, y).Y);

        var vial = new byte[16];
        vial[1] = 6;
        Assert.True(QmkVialProtocol.TryGetVialRgbCompiledFlag(vial, out var compiled));
        Assert.False(compiled);
        vial[13] = 1;
        Assert.True(QmkVialProtocol.TryGetVialRgbCompiledFlag(vial, out compiled));
        Assert.True(compiled);
    }

    [Fact]
    public void Qmk_direct_packets_allocate_nothing_after_warmup()
    {
        RgbColor[] colors = [new RgbColor(255, 0, 0), new RgbColor(0, 255, 0), new RgbColor(0, 0, 255)];
        var packet = new byte[QmkVialProtocol.ReportLength];
        QmkVialProtocol.WriteDirectPacket(0, colors, packet);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            QmkVialProtocol.WriteDirectPacket((ushort)i, colors, packet);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Logitech_long_report_is_20_bytes_and_allocates_nothing()
    {
        var report = new byte[LogitechHidPpProtocol.ReportLength];
        LogitechHidPpProtocol.WriteGetFeature(LogitechHidPpProtocol.WiredDeviceIndex, LogitechHidPpProtocol.FeatureRgbEffects, report);
        Assert.Equal(0x11, report[0]);
        Assert.Equal(0xFF, report[1]);
        Assert.Equal(0x00, report[2]);
        Assert.Equal(0x0A, report[3]);
        Assert.Equal(0x80, report[4]);
        Assert.Equal(0x71, report[5]);
        Assert.Equal(20, report.Length);

        var color = new RgbColor(9, 8, 7);
        LogitechHidPpProtocol.WriteSetZoneEffect(0xFF, 0x05, 1, color, report);
        Assert.Equal(0x11, report[0]);
        Assert.Equal(0xFF, report[1]);
        Assert.Equal(0x05, report[2]);
        Assert.Equal(0x1A, report[3]);
        Assert.Equal(1, report[4]);
        Assert.Equal(0x01, report[5]);
        Assert.Equal(9, report[6]);
        Assert.Equal(8, report[7]);
        Assert.Equal(7, report[8]);
        Assert.Equal(0, report[19]);

        LogitechHidPpProtocol.WriteSoftwareControl(0xFF, 0x05, rgbEffects8071: true, report);
        Assert.Equal(0x5A, report[3]);
        Assert.Equal(0x00, report[6]);

        byte[] error = [0x11, 0xFF, 0xFF, 0x0A, 0x8F, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.False(LogitechHidPpProtocol.TryParseGetFeatureResponse(error, out _, out var invalidDevice));
        Assert.True(invalidDevice);

        byte[] ok = [0x11, 0xFF, 0x00, 0x0A, 0x0D, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.True(LogitechHidPpProtocol.TryParseGetFeatureResponse(ok, out var feature, out invalidDevice));
        Assert.Equal(0x0D, feature);
        Assert.False(invalidDevice);

        LogitechHidPpProtocol.WriteSetZoneEffect(0x01, 0x0D, 0, color, report);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            LogitechHidPpProtocol.WriteSetZoneEffect(0x01, 0x0D, (byte)(i & 1), color, report);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Razer_crc_covers_payload_bytes_2_through_87_without_allocating()
    {
        var payload = new byte[RazerChromaProtocol.PayloadLength];
        RgbColor[] colors = [new RgbColor(1, 2, 3), new RgbColor(4, 5, 6)];
        RazerChromaProtocol.WriteMatrixCustomFrame(0, 0, colors, payload);

        Assert.Equal(0x1F, payload[1]);
        Assert.Equal(0x0F, payload[6]);
        Assert.Equal(0x03, payload[7]);
        Assert.Equal(0, payload[8]);
        Assert.Equal(0, payload[9]);
        Assert.Equal(1, payload[10]);
        Assert.Equal(1, payload[11]);
        Assert.Equal(2, payload[12]);
        Assert.Equal(3, payload[13]);
        Assert.Equal(RazerChromaProtocol.ComputeCrc(payload), payload[88]);
        Assert.Equal(0, payload[89]);

        byte xor = 0;
        for (var i = 2; i <= 87; i++)
        {
            xor ^= payload[i];
        }

        Assert.Equal(xor, payload[88]);

        var report = new byte[RazerChromaProtocol.ReportLength];
        RazerChromaProtocol.WriteStaticEffect(report);
        Assert.Equal(0x00, report[0]);
        Assert.Equal(0x1F, report[2]);
        Assert.Equal(0x0F, report[7]);
        Assert.Equal(0x02, report[8]);
        Assert.Equal(0x08, report[11]);
        Assert.Equal(RazerChromaProtocol.ComputeCrc(report.AsSpan(1, 90)), report[89]);
        Assert.Equal(0, report[90]);

        RazerChromaProtocol.WriteStaticEffect(payload);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            RazerChromaProtocol.WriteMatrixCustomFrame(0, 0, colors, payload);
            RazerChromaProtocol.WriteStaticEffect(report);
            _ = RazerChromaProtocol.ComputeCrc(payload);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Theory]
    [InlineData(0x48)]
    [InlineData(0x4F)]
    [InlineData(0x50)]
    [InlineData(0x57)]
    [InlineData(0x58)]
    public void Ene_slave_guard_rejects_pmic_spd_and_non_ene_addresses(byte address)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EneDramProtocol.ValidateSlave(address));
    }

    [Theory]
    [InlineData(0x39)]
    [InlineData(0x3C)]
    [InlineData(0x70)]
    [InlineData(0x77)]
    public void Ene_slave_guard_allows_the_documented_rgb_addresses(byte address)
    {
        var ex = Record.Exception(() => EneDramProtocol.ValidateSlave(address));
        Assert.Null(ex);
        SmbusAddressFirewall.ValidateAddress(address);
    }

    [Fact]
    public void Ene_probe_addresses_never_enter_the_spd_or_pmic_ranges()
    {
        Assert.NotEmpty(EneDramProtocol.ProbeAddresses.ToArray());
        foreach (var address in EneDramProtocol.ProbeAddresses)
        {
            Assert.False(address is >= 0x48 and <= 0x57);
            EneDramProtocol.ValidateSlave(address);
        }
    }

    [Fact]
    public void Ene_signature_mismatch_never_writes_the_data_port()
    {
        var bus = new EneFakeBus();
        bus.Seed(0x71, "NOPE------------");

        var result = EneDramProtocol.ProbeAddress(bus, 0x71);

        Assert.Equal(EneSlotProbeStatus.SignatureMismatch, result.Status);
        Assert.DoesNotContain(bus.Writes, write => write.Register == EneDramProtocol.DataRegister);
        Assert.All(bus.Writes, write => Assert.False(write.Address is >= 0x48 and <= 0x57));
    }

    [Fact]
    public void Ene_matching_signature_is_accepted_and_frames_are_diff_cached_without_allocating()
    {
        var bus = new EneFakeBus();
        bus.Seed(0x71, "AUDA0-E6K5-0101");
        var result = EneDramProtocol.ProbeAddress(bus, 0x71);
        Assert.Equal(EneSlotProbeStatus.Supported, result.Status);
        Assert.StartsWith("AUDA", result.Signature);
        Assert.DoesNotContain(bus.Writes, write => write.Register == EneDramProtocol.DataRegister);

        byte[] addresses = [0x71];
        var colors = new RgbColor[EneDramProtocol.LedsPerDimm];
        colors[0] = new RgbColor(1, 2, 3);
        var cache = new byte?[1, EneDramProtocol.CacheWidth(EneDramProtocol.LedsPerDimm)];
        var destination = new EneRegisterWrite[EneDramProtocol.CacheWidth(EneDramProtocol.LedsPerDimm)];

        var written = EneDramProtocol.BuildFrameWrites(addresses, colors, EneDramProtocol.LedsPerDimm, cache, destination);
        Assert.Equal(EneDramProtocol.CacheWidth(EneDramProtocol.LedsPerDimm), written);
        Assert.Equal(EneDramProtocol.RegisterMode, destination[0].Register);
        Assert.Equal(EneDramProtocol.Enable, destination[0].Value);
        Assert.Equal(EneDramProtocol.RegisterDirect, destination[1].Register);
        Assert.Equal(EneDramProtocol.RegisterColorBase, destination[2].Register);
        Assert.Equal(1, destination[2].Value);
        Assert.Equal((ushort)(EneDramProtocol.RegisterColorBase + 1), destination[3].Register);
        Assert.Equal(3, destination[3].Value);
        Assert.Equal((ushort)(EneDramProtocol.RegisterColorBase + 2), destination[4].Register);
        Assert.Equal(2, destination[4].Value);

        for (var i = 0; i < written; i++)
        {
            cache[destination[i].Slot, destination[i].CacheIndex] = destination[i].Value;
        }

        Assert.Equal(0, EneDramProtocol.BuildFrameWrites(addresses, colors, EneDramProtocol.LedsPerDimm, cache, destination));
        colors[0] = new RgbColor(9, 2, 3);
        var changed = EneDramProtocol.BuildFrameWrites(addresses, colors, EneDramProtocol.LedsPerDimm, cache, destination);
        Assert.Equal(1, changed);
        Assert.Equal(EneDramProtocol.RegisterColorBase, destination[0].Register);
        Assert.Equal(9, destination[0].Value);

        colors[0] = new RgbColor(1, 2, 3);
        for (var i = 0; i < 40; i++)
        {
            EneDramProtocol.BuildFrameWrites(addresses, colors, EneDramProtocol.LedsPerDimm, cache, destination);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            EneDramProtocol.BuildFrameWrites(addresses, colors, EneDramProtocol.LedsPerDimm, cache, destination);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Throws<ArgumentOutOfRangeException>(() => EneDramProtocol.BuildFrameWrites([0x50], colors, EneDramProtocol.LedsPerDimm, cache, destination));
    }

    [Fact]
    public async Task Ene_probe_does_not_open_smbus_without_a_matching_part()
    {
        var bus = new EneFakeBus();
        var factory = new FakeSmbusFactory([Stick("Kingston", "KF556C40-32")], bus);
        var probe = new EneDramProbe(factory, Options.Create(new HardwareOptions()), NullLogger<EneDramProbe>.Instance);

        var devices = await probe.ProbeAsync(TestContext.Current.CancellationToken);

        Assert.Empty(devices);
        Assert.Equal(0, factory.OpenCount);
        Assert.Empty(bus.Writes);
    }

    [Theory]
    [InlineData(0x48)]
    [InlineData(0x4F)]
    [InlineData(0x50)]
    [InlineData(0x57)]
    [InlineData(0x70)]
    public void Corsair_slave_guard_rejects_spd_pmic_and_ene_addresses(byte address)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CorsairDramProtocol.ValidateSlave(address));
    }

    [Fact]
    public void Corsair_signature_mismatch_writes_nothing_and_frames_are_diff_cached_without_allocating()
    {
        var bus = new CorsairFakeBus();
        bus.Seed(0x58, vendor: 0x00, model: 0x04);
        var rejected = CorsairDramProtocol.ProbeSlot(bus, 0);
        Assert.Equal(CorsairSlotProbeStatus.SignatureMismatch, rejected.Status);
        Assert.Empty(bus.Writes);

        bus.Seed(0x58, CorsairDramProtocol.VendorId, CorsairDramProtocol.ModelId);
        var accepted = CorsairDramProtocol.ProbeSlot(bus, 0);
        Assert.Equal(CorsairSlotProbeStatus.Supported, accepted.Status);
        Assert.Equal((byte)0x58, accepted.Address);
        Assert.Empty(bus.Writes);
        Assert.Throws<ArgumentOutOfRangeException>(() => CorsairDramProtocol.ValidateRead(0x58, CorsairDramProtocol.RegisterDirect));
        Assert.Throws<ArgumentOutOfRangeException>(() => CorsairDramProtocol.ValidateWrite(0x58, 0x43, 0x1B));

        byte[] addresses = [0x58];
        var colors = new RgbColor[CorsairDramProtocol.LedsPerDimm];
        colors[0] = new RgbColor(8, 9, 10);
        var cache = new byte?[1, 256];
        var destination = new CorsairRegisterWrite[1 + (CorsairDramProtocol.LedsPerDimm * 3) + 1];
        var written = CorsairDramProtocol.BuildFrameWrites(addresses, colors, CorsairDramProtocol.LedsPerDimm, cache, destination);
        Assert.Equal(1 + (CorsairDramProtocol.LedsPerDimm * 3) + 1, written);
        Assert.Equal(CorsairDramProtocol.RegisterDirect, destination[0].Register);
        Assert.Equal(CorsairDramProtocol.DirectEnable, destination[0].Value);
        Assert.Equal(CorsairDramProtocol.ColorBase, destination[1].Register);
        Assert.Equal(8, destination[1].Value);
        Assert.Equal(9, destination[2].Value);
        Assert.Equal(10, destination[3].Value);
        Assert.Equal(CorsairDramProtocol.RegisterApply, destination[written - 1].Register);
        Assert.False(destination[written - 1].Remember);

        for (var i = 0; i < written; i++)
        {
            if (destination[i].Remember)
            {
                cache[destination[i].Slot, destination[i].Register] = destination[i].Value;
            }
        }

        Assert.Equal(0, CorsairDramProtocol.BuildFrameWrites(addresses, colors, CorsairDramProtocol.LedsPerDimm, cache, destination));
        colors[1] = new RgbColor(1, 0, 0);
        var changed = CorsairDramProtocol.BuildFrameWrites(addresses, colors, CorsairDramProtocol.LedsPerDimm, cache, destination);
        Assert.Equal(2, changed);
        Assert.Equal(CorsairDramProtocol.RegisterApply, destination[1].Register);

        colors[1] = default;
        for (var i = 0; i < 40; i++)
        {
            CorsairDramProtocol.BuildFrameWrites(addresses, colors, CorsairDramProtocol.LedsPerDimm, cache, destination);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            CorsairDramProtocol.BuildFrameWrites(addresses, colors, CorsairDramProtocol.LedsPerDimm, cache, destination);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Throws<ArgumentOutOfRangeException>(() => CorsairDramProtocol.BuildFrameWrites([0x50], colors, CorsairDramProtocol.LedsPerDimm, cache, destination));
    }

    [Fact]
    public async Task Corsair_probe_does_not_open_smbus_without_a_matching_part()
    {
        var bus = new CorsairFakeBus();
        var factory = new FakeSmbusFactory([Stick("G.Skill", "F4-3600C16D-16GTZR")], bus);
        var probe = new CorsairDramProbe(factory, Options.Create(new HardwareOptions()), NullLogger<CorsairDramProbe>.Instance);

        var devices = await probe.ProbeAsync(TestContext.Current.CancellationToken);

        Assert.Empty(devices);
        Assert.Equal(0, factory.OpenCount);
        Assert.Empty(bus.Writes);
    }

    [Theory]
    [InlineData("G.Skill", "F4-3600C16D-16GTZR", true, false)]
    [InlineData("G.Skill", "F4-3200C16D-16GTZN", true, false)]
    [InlineData("G.Skill", "F5-6000J3636F16GX2-TZ5NR", true, false)]
    [InlineData("Crucial", "BL8G36C16U4RGB", true, false)]
    [InlineData("TeamGroup", "TF10D432G3200HC16CDC01", true, false)]
    [InlineData("TeamGroup", "FF3D532G6000HC38", true, false)]
    [InlineData("ASUS", "AURA-RGB-16G", true, false)]
    [InlineData("Kingston", "KF556C40-32", false, false)]
    [InlineData("Corsair", "CMH32GX5M2E6000C36", false, true)]
    [InlineData("Corsair", "CMW16GX4M2C3200C16", false, true)]
    [InlineData("Corsair", "CMT32GX4M2C3200C16", false, true)]
    [InlineData("Corsair", "CMP32GX5M2B6000C30", false, true)]
    [InlineData("Corsair", "DOMINATOR PLATINUM RGB", false, true)]
    public void Smbios_part_numbers_select_ene_and_corsair_families(string manufacturer, string part, bool ene, bool corsair)
    {
        var stick = Stick(manufacturer, part);
        Assert.Equal(ene, stick.IsEneCompatibleDram);
        Assert.Equal(corsair, stick.IsCorsairDram);
    }

    [Fact]
    public void OpenRgb_header_and_controller_blob_round_trip()
    {
        var header = new byte[OpenRgbProtocol.HeaderLength];
        OpenRgbProtocol.WriteHeader(header, 3, OpenRgbProtocol.UpdateLeds, 8);
        Assert.Equal((byte)'O', header[0]);
        Assert.Equal((byte)'R', header[1]);
        Assert.Equal((byte)'G', header[2]);
        Assert.Equal((byte)'B', header[3]);
        Assert.True(OpenRgbProtocol.TryParseHeader(header, out var device, out var packetId, out var length));
        Assert.Equal(3u, device);
        Assert.Equal(OpenRgbProtocol.UpdateLeds, packetId);
        Assert.Equal(8, length);

        var info = new OpenRgbControllerInfo("Desk Strip", "OpenRGB", "test", "1.0", "SER", "tcp:6742", 1, 2);
        var blob = new byte[1024];
        var written = OpenRgbProtocol.WriteControllerData(info, blob);
        Assert.True(OpenRgbProtocol.TryParseControllerData(blob.AsSpan(0, written), out var parsed));
        Assert.Equal(info.Name, parsed.Name);
        Assert.Equal(info.Vendor, parsed.Vendor);
        Assert.Equal(2, parsed.LedCount);
        Assert.Equal(1, parsed.ZoneCount);

        var frame = new byte[OpenRgbProtocol.HeaderLength + 8];
        var count = OpenRgbProtocol.WriteUpdateLeds(3, [new RgbColor(1, 2, 3), new RgbColor(4, 5, 6)], frame);
        Assert.Equal(24, count);
        Assert.Equal((byte)'O', frame[0]);
        Assert.Equal(1, frame[16]);
        Assert.Equal(2, frame[17]);
        Assert.Equal(3, frame[18]);
        Assert.Equal(0, frame[19]);
        Assert.Equal(4, frame[20]);
        Assert.Equal(5, frame[21]);
        Assert.Equal(6, frame[22]);
        Assert.Equal(0, frame[23]);

        var previousEndpoint = Environment.GetEnvironmentVariable("RIGFORGE_OPENRGB_ENDPOINT");
        Environment.SetEnvironmentVariable("RIGFORGE_OPENRGB_ENDPOINT", null);
        try
        {
            Assert.False(OpenRgbProtocol.TryGetEndpoint(new MapConfiguration(), out _, out _));
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIGFORGE_OPENRGB_ENDPOINT", previousEndpoint);
        }
        Assert.True(OpenRgbProtocol.TryParseEndpoint("127.0.0.1:6742", out var host, out var port));
        Assert.Equal("127.0.0.1", host);
        Assert.Equal(6742, port);
    }

    [Fact]
    public async Task OpenRgb_loopback_pushes_an_rgb_frame_with_no_steady_state_allocations()
    {
        var info = new OpenRgbControllerInfo("Test Strip", "RigForge", "loopback", "1", "S", "net", 1, 2);
        var blob = new byte[1024];
        var blobLength = OpenRgbProtocol.WriteControllerData(info, blob);
        var controller = blob.AsSpan(0, blobLength).ToArray();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var frames = new List<byte[]>();
        var server = Task.Run(() => RunOpenRgbServer(listener, controller, frames), TestContext.Current.CancellationToken);
        OpenRgbBridgeDevice? device = null;
        var token = TestContext.Current.CancellationToken;
        try
        {
            var devices = await OpenRgbBridgeDevice.ConnectAllAsync("127.0.0.1", port, dryRun: false, token);
            device = Assert.Single(devices);
            Assert.Equal("Test Strip", device.Name);
            Assert.Equal(2, device.Zones[0].LedCount);

            RgbColor[] colors = [new RgbColor(10, 20, 30), new RgbColor(40, 50, 60)];
            await device.UpdateLedsAsync(colors, token);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 40; i++)
            {
                await device.UpdateLedsAsync(colors, token);
            }

            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (FrameCount(frames) == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10, token);
            }

            Assert.NotEmpty(frames);
            var frame = frames[0];
            Assert.Equal(8, frame.Length);
            Assert.Equal((byte)10, frame[0]);
            Assert.Equal((byte)20, frame[1]);
            Assert.Equal((byte)30, frame[2]);
            Assert.Equal((byte)0, frame[3]);
            Assert.Equal((byte)40, frame[4]);
            Assert.Equal((byte)50, frame[5]);
            Assert.Equal((byte)60, frame[6]);
            Assert.Equal((byte)0, frame[7]);
        }
        finally
        {
            if (device is not null)
            {
                await device.DisposeAsync();
            }

            listener.Stop();
            await server.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        }
    }

    private static int FrameCount(List<byte[]> frames)
    {
        lock (frames)
        {
            return frames.Count;
        }
    }

    [Fact]
    public void Unmanaged_controller_has_no_capabilities_and_skips_known_protocols()
    {
        var device = new UnmanagedRgbControllerDevice(
            0x1038,
            0x12E0,
            "SteelSeries Arctis Nova Pro Wireless",
            "SteelSeries",
            @"\\?\hid#vid_1038&pid_12e0",
            "USB HID",
            "0xFF00",
            "ABC123");

        Assert.Equal(DeviceSupportStatus.Unmanaged, device.SupportStatus);
        Assert.Equal(DeviceCapabilities.None, device.Capabilities);
        Assert.Contains("not yet implemented", device.StatusReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("1038:12E0", device.Properties["vidPid"]);
        Assert.Equal("USB HID", device.Properties["transport"]);
        Assert.Equal(@"\\?\hid#vid_1038&pid_12e0", device.Properties["path"]);
        Assert.Equal("0xFF00", device.Properties["usagePage"]);

        var nvidia = UnmanagedRgbControllerDevice.ForNvidiaPartner("NVIDIA GeForce RTX 5070 Ti", "nvml:0");
        Assert.Equal(DeviceSupportStatus.Unmanaged, nvidia.SupportStatus);
        Assert.Equal(DeviceCapabilities.None, nvidia.Capabilities);
        Assert.Contains("NVML", nvidia.StatusReason, StringComparison.Ordinal);

        const uint steelSeriesVendorUsage = 0xFF000001;
        Assert.True(UnmanagedRgbControllerProbe.IsUnmanagedCandidate(0x1038, 0x12E0, steelSeriesVendorUsage, 64, 0, null));
        Assert.False(UnmanagedRgbControllerProbe.IsUnmanagedCandidate(0x046D, 0xC08B, 0xFF000002, 20, 0, null));
        Assert.False(UnmanagedRgbControllerProbe.IsUnmanagedCandidate(0x1532, 0x0084, 0xFF000001, 91, 0, null));
        Assert.False(UnmanagedRgbControllerProbe.IsUnmanagedCandidate(0x054C, 0x0CE6, steelSeriesVendorUsage, 64, 0, null));
        Assert.False(UnmanagedRgbControllerProbe.IsUnmanagedCandidate(0x1038, 0x12E0, steelSeriesVendorUsage, 0, 0, null));
        Assert.False(UnmanagedRgbControllerProbe.IsUnmanagedCandidate(
            0x3434,
            0x0001,
            ((uint)QmkVialProtocol.UsagePage << 16) | QmkVialProtocol.Usage,
            33,
            0,
            null));
        Assert.True(UnmanagedRgbControllerProbe.IsKnownSupported(NzxtCatalogVendor, 0x300E, null));
    }

    [Fact]
    public void Unmanaged_probe_rejects_short_reports_and_usb_audio_endpoints()
    {
        const uint vendorUsage = 0xFFC00001;

        Assert.True(UnmanagedRgbControllerProbe.IsUnmanagedCandidate(0x1038, 0x12E0, vendorUsage, 8, 0, null, "Arctis Nova Pro Wireless"));
        Assert.True(UnmanagedRgbControllerProbe.IsUnmanagedCandidate(0x1038, 0x12E0, vendorUsage, 0, 8, null, "Arctis Nova Pro Wireless"));
        Assert.False(UnmanagedRgbControllerProbe.IsUnmanagedCandidate(0x1038, 0x12E0, vendorUsage, 7, 0, null, "Arctis Nova Pro Wireless"));
        Assert.False(UnmanagedRgbControllerProbe.IsUnmanagedCandidate(0x1038, 0x12E0, vendorUsage, 0, 7, null, "Arctis Nova Pro Wireless"));
        Assert.False(UnmanagedRgbControllerProbe.IsUnmanagedCandidate(0x0B05, 0x1B9B, vendorUsage, 2, 0, null, "Generic USB Audio"));
        Assert.False(UnmanagedRgbControllerProbe.IsUnmanagedCandidate(0x0B05, 0x1B9B, vendorUsage, 64, 0, null, "USB Audio"));
        Assert.False(UnmanagedRgbControllerProbe.IsUnmanagedCandidate(0x0B05, 0x1B9B, vendorUsage, 64, 16, null, "usb audio"));
        Assert.False(UnmanagedRgbControllerProbe.IsUnmanagedCandidate(0x0B05, 0x1B9B, vendorUsage, 0, 64, null, "  Generic USB Audio  "));
        Assert.True(UnmanagedRgbControllerProbe.IsUnmanagedCandidate(0x0B05, 0x1B9B, vendorUsage, 64, 0, null, "Prime RGB Header"));

        var audioOnly = UnmanagedRgbControllerProbe.CollapseCollections(
        [
            Candidate(0x0B05, 0x1B9B, "", AudioPath, "Generic USB Audio", 2, 0),
            Candidate(0x0B05, 0x1B9B, "", AudioPath, "USB Audio", 4, 0),
        ]);
        Assert.Empty(audioOnly);
    }

    [Fact]
    public void Unmanaged_probe_collapses_multi_collection_devices_onto_the_widest_report()
    {
        const string col01 = @"\\?\HID#VID_1038&PID_12E0&MI_04&Col01#7&1a2b3c4d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
        const string col02 = @"\\?\hid#vid_1038&pid_12e0&mi_04&col02#8&deadbeef&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}";
        const string container = @"\\?\hid#vid_1038&pid_12e0&mi_04";

        Assert.Equal(container, UnmanagedRgbControllerProbe.PhysicalContainerOrProductKey(col01, "Arctis Nova Pro Wireless"));
        Assert.Equal(container, UnmanagedRgbControllerProbe.PhysicalContainerOrProductKey(col02, "Arctis Nova Pro Wireless"));
        Assert.Equal(container, UnmanagedRgbControllerProbe.UnmanagedInstanceKey(null, col01, "Arctis Nova Pro Wireless"));
        Assert.Equal(container, UnmanagedRgbControllerProbe.UnmanagedInstanceKey("   ", col02, "Arctis Nova Pro Wireless"));
        Assert.Equal("ABC123", UnmanagedRgbControllerProbe.UnmanagedInstanceKey(" ABC123 ", col01, "Arctis Nova Pro Wireless"));
        Assert.Equal("Arctis Nova Pro Wireless", UnmanagedRgbControllerProbe.PhysicalContainerOrProductKey(null, " Arctis Nova Pro Wireless "));
        Assert.DoesNotContain("col0", container, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("4d1e55b2", container, StringComparison.OrdinalIgnoreCase);

        var collapsed = UnmanagedRgbControllerProbe.CollapseCollections(
        [
            Candidate(0x1038, 0x12E0, "", col01, "Arctis Nova Pro Wireless", 32, 0),
            Candidate(0x1038, 0x12E0, "", col02, "Arctis Nova Pro Wireless", 8, 64),
            Candidate(0x1038, 0x12E0, "LEFT", col01, "Arctis Nova Pro Wireless", 64, 0),
            Candidate(0x1038, 0x12E0, "RIGHT", col02, "Arctis Nova Pro Wireless", 16, 0),
            Candidate(0x1038, 0x12E0, "", @"\\?\hid#vid_1038&pid_12e0&mi_00&col01#7&ffff&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", "Arctis Nova Pro Wireless", 16, 0),
        ]);

        Assert.Equal(4, collapsed.Count);
        var headset = Assert.Single(collapsed, item => item.Serial.Length == 0 && item.DevicePath.Contains("mi_04", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(col02, headset.DevicePath);
        Assert.Equal(64, headset.FeatureReportLength);
        Assert.Equal(8, headset.OutputReportLength);
        Assert.Contains(collapsed, item => item.Serial == "LEFT" && item.OutputReportLength == 64);
        Assert.Contains(collapsed, item => item.Serial == "RIGHT" && item.OutputReportLength == 16);
        Assert.Contains(collapsed, item => item.DevicePath.Contains("mi_00", StringComparison.OrdinalIgnoreCase));

        var sameSerial = UnmanagedRgbControllerProbe.CollapseCollections(
        [
            Candidate(0x1038, 0x12E0, "SN", col01, "Arctis Nova Pro Wireless", 32, 0),
            Candidate(0x1038, 0x12E0, "SN", col02, "Arctis Nova Pro Wireless", 64, 8),
        ]);
        var serialWinner = Assert.Single(sameSerial);
        Assert.Equal(64, serialWinner.OutputReportLength);
        Assert.Equal(col02, serialWinner.DevicePath);

        var namedOnly = UnmanagedRgbControllerProbe.CollapseCollections(
        [
            Candidate(0x1038, 0x12E0, "", "", "Arctis Nova Pro Wireless", 16, 0),
            Candidate(0x1038, 0x12E0, "", "   ", "Arctis Nova Pro Wireless", 64, 0),
        ]);
        var named = Assert.Single(namedOnly);
        Assert.Equal(64, named.OutputReportLength);
    }

    private const string AudioPath = @"\\?\hid#vid_0b05&pid_1b9b&mi_07#7&aaa&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    private static UnmanagedRgbControllerProbe.UnmanagedInterfaceCandidate Candidate(
        int vendorId,
        int productId,
        string serial,
        string devicePath,
        string productName,
        int outputReportLength,
        int featureReportLength) =>
        new(vendorId, productId, serial, devicePath, productName, outputReportLength, featureReportLength);

    private const int NzxtCatalogVendor = 0x1E71;

    private static void RunOpenRgbServer(TcpListener listener, byte[] controller, List<byte[]> frames)
    {
        try
        {
            using var client = listener.AcceptTcpClient();
            client.NoDelay = true;
            var stream = client.GetStream();
            var header = new byte[OpenRgbProtocol.HeaderLength];
            while (ReadExact(stream, header))
            {
                if (!OpenRgbProtocol.TryParseHeader(header, out var deviceIndex, out var packetId, out var length))
                {
                    break;
                }

                var body = length == 0 ? [] : new byte[length];
                if (length > 0 && !ReadExact(stream, body))
                {
                    break;
                }

                if (packetId == OpenRgbProtocol.RequestProtocolVersion)
                {
                    WritePacket(stream, deviceIndex, packetId, [4, 0, 0, 0]);
                }
                else if (packetId == OpenRgbProtocol.RequestControllerCount)
                {
                    WritePacket(stream, 0, packetId, [1, 0, 0, 0]);
                }
                else if (packetId == OpenRgbProtocol.RequestControllerData)
                {
                    WritePacket(stream, deviceIndex, packetId, controller);
                }
                else if (packetId == OpenRgbProtocol.UpdateLeds)
                {
                    lock (frames)
                    {
                        frames.Add(body);
                    }
                }
            }
        }
        catch (Exception)
        {
            // The client closes the socket at the end of the test.
        }
    }

    private static void WritePacket(NetworkStream stream, uint deviceIndex, uint packetId, byte[] payload)
    {
        var header = new byte[OpenRgbProtocol.HeaderLength];
        OpenRgbProtocol.WriteHeader(header, deviceIndex, packetId, payload.Length);
        stream.Write(header);
        if (payload.Length > 0)
        {
            stream.Write(payload);
        }
    }

    private static bool ReadExact(NetworkStream stream, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = stream.Read(buffer, read, buffer.Length - read);
            if (count <= 0)
            {
                return false;
            }

            read += count;
        }

        return true;
    }

    private static SmbiosMemoryStick Stick(string manufacturer, string part) =>
        new("BANK 0", "DIMM A1", manufacturer, part, "SERIAL", 16384, 6000);

    private sealed class MapConfiguration : IConfigurationView
    {
        public string? this[string key] => null;
    }

    private sealed class FakeSmbusFactory(IReadOnlyList<SmbiosMemoryStick> sticks, ISmbusController controller) : ISmbusControllerFactory
    {
        public string BackendName => "Fake";
        public bool IsAvailable => true;
        public string DiagnosticStatus => "Ready";
        public int OpenCount { get; private set; }

        public IReadOnlyList<SmbiosMemoryStick> GetInstalledMemory() => sticks;

        public IReadOnlyList<ISmbusController> OpenControllers()
        {
            OpenCount++;
            return [controller];
        }
    }

    private sealed class EneFakeBus : ISmbusController
    {
        private readonly HashSet<byte> _present = [];
        private readonly Dictionary<(byte Address, ushort Register), byte> _memory = [];
        private byte _high;
        private bool _highStaged;
        private ushort _pointer;

        public string BusName => "piix4-0";
        public string Location => "Fake PIIX4";
        public List<(byte Address, byte Register, byte Value)> Writes { get; } = [];

        public void Seed(byte address, string name)
        {
            _present.Add(address);
            for (var i = 0; i < name.Length && i < EneDramProtocol.NameLength; i++)
            {
                _memory[(address, (ushort)(EneDramProtocol.RegisterName + i))] = (byte)name[i];
            }
        }

        public IDisposable AcquireBusLock() => Noop.Instance;

        public bool TryWriteByteData(byte address, byte register, byte value)
        {
            SmbusAddressFirewall.ValidateAddress(address);
            Writes.Add((address, register, value));
            if (!_present.Contains(address))
            {
                return false;
            }

            if (register == EneDramProtocol.AddressRegister)
            {
                if (!_highStaged)
                {
                    _high = value;
                    _highStaged = true;
                }
                else
                {
                    _pointer = (ushort)((_high << 8) | value);
                    _highStaged = false;
                }

                return true;
            }

            if (register == EneDramProtocol.DataRegister)
            {
                _memory[(address, _pointer)] = value;
                return true;
            }

            return false;
        }

        public bool TryReadWordData(byte address, byte register, out ushort value)
        {
            SmbusAddressFirewall.ValidateAddress(address);
            value = 0xFFFF;
            if (register == EneDramProtocol.ValueRegister && _present.Contains(address))
            {
                _memory.TryGetValue((address, _pointer), out var stored);
                value = stored;
                return true;
            }

            return false;
        }

        public void Dispose()
        {
        }
    }

    private sealed class CorsairFakeBus : ISmbusController
    {
        private readonly HashSet<byte> _present = [];
        private readonly Dictionary<(byte Address, byte Register), ushort> _words = [];

        public string BusName => "piix4-0";
        public string Location => "Fake PIIX4";
        public List<(byte Address, byte Register, byte Value)> Writes { get; } = [];

        public void Seed(byte address, byte vendor, byte model)
        {
            _present.Add(address);
            _words[(address, CorsairDramProtocol.RegisterVendor)] = vendor;
            _words[(address, CorsairDramProtocol.RegisterModel)] = model;
        }

        public IDisposable AcquireBusLock() => Noop.Instance;

        public bool TryWriteByteData(byte address, byte register, byte value)
        {
            SmbusAddressFirewall.ValidateAddress(address);
            Writes.Add((address, register, value));
            return _present.Contains(address);
        }

        public bool TryReadWordData(byte address, byte register, out ushort value)
        {
            SmbusAddressFirewall.ValidateAddress(address);
            return _words.TryGetValue((address, register), out value);
        }

        public void Dispose()
        {
        }
    }

    private sealed class Noop : IDisposable
    {
        public static readonly Noop Instance = new();
        public void Dispose()
        {
        }
    }
}
