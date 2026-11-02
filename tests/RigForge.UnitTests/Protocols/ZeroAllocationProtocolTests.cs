using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Asus;
using RigForge.Infrastructure.CrossPlatform.Kingston;
using RigForge.Infrastructure.CrossPlatform.Nzxt;

namespace RigForge.UnitTests.Protocols;

public sealed class ZeroAllocationProtocolTests
{
    [Fact]
    public void Aura_direct_packet_writes_allocate_nothing_after_warmup()
    {
        var colors = Enumerable.Range(0, 25).Select(i => new RgbColor((byte)i, 1, 2)).ToArray();
        var packet = new byte[AuraUsbProtocol.ReportLength];
        var packetCount = AuraUsbProtocol.PacketCount(colors.Length);

        // Warmup: JIT and any one-time initialization stay outside the measurement.
        for (var i = 0; i < packetCount; i++)
        {
            AuraUsbProtocol.WriteDirectPacket(1, colors, i, packet);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < 100; iteration++)
        {
            for (var i = 0; i < packetCount; i++)
            {
                AuraUsbProtocol.WriteDirectPacket(1, colors, i, packet);
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Hue2_packet_writes_allocate_nothing_after_warmup()
    {
        var colors = Enumerable.Range(0, 25).Select(i => new RgbColor((byte)i, (byte)(100 + i), 200)).ToArray();
        var packet = new byte[NzxtProtocol.ReportLength];
        var packetCount = NzxtProtocol.Hue2PacketCount(colors.Length);

        for (var i = 0; i < packetCount; i++)
        {
            NzxtProtocol.WriteHue2Packet(1, i, colors, packet);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < 100; iteration++)
        {
            for (var i = 0; i < packetCount; i++)
            {
                NzxtProtocol.WriteHue2Packet(1, i, colors, packet);
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Kingston_frame_writes_allocate_nothing_after_warmup()
    {
        byte[] slotAddresses = [0x61, 0x63];
        var colors = Enumerable.Range(0, 24).Select(i => new RgbColor((byte)i, (byte)(i + 10), (byte)(i + 20))).ToArray();
        var registerCache = new byte?[slotAddresses.Length, 256];
        var destination = new KingstonRegisterWrite[slotAddresses.Length * (3 + KingstonFuryDramProtocol.LedsPerDimmDdr5 * 3)];

        var written = 0;
        // Extended warmup lets tiered JIT promotion finish before the measurement.
        for (var iteration = 0; iteration < 100; iteration++)
        {
            written = KingstonFuryDramProtocol.BuildFrameWrites(slotAddresses, colors, 80, registerCache, destination);
        }

        Assert.Equal(78, written);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < 100; iteration++)
        {
            written = KingstonFuryDramProtocol.BuildFrameWrites(slotAddresses, colors, 80, registerCache, destination);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(78, written);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Kingston_frame_writes_reject_a_too_small_destination()
    {
        byte[] slotAddresses = [0x61];
        var colors = new RgbColor[KingstonFuryDramProtocol.LedsPerDimmDdr5];
        var registerCache = new byte?[1, 256];
        var destination = new KingstonRegisterWrite[1];

        Assert.Throws<ArgumentException>(() =>
            KingstonFuryDramProtocol.BuildFrameWrites(slotAddresses, colors, 80, registerCache, destination));
    }
}
