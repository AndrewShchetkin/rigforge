using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Gpu;
using RigForge.Infrastructure.Linux.Gpu;
using RigForge.Infrastructure.Linux.Udev;
using RigForge.Infrastructure.Windows.Gpu;

namespace RigForge.UnitTests.Platform;

public sealed class GpuI2cTransportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rigforge-gpu-i2c-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ------------------------------------------------------------------ NVAPI ABI

    [Fact]
    public void NvI2cInfoV3_matches_the_x64_nvapi_layout()
    {
        Assert.Equal(64, Marshal.SizeOf<NvI2cInfoV3>());
        Assert.Equal(0, (int)Marshal.OffsetOf<NvI2cInfoV3>(nameof(NvI2cInfoV3.Version)));
        Assert.Equal(4, (int)Marshal.OffsetOf<NvI2cInfoV3>(nameof(NvI2cInfoV3.DisplayMask)));
        Assert.Equal(8, (int)Marshal.OffsetOf<NvI2cInfoV3>(nameof(NvI2cInfoV3.IsDdcPort)));
        Assert.Equal(9, (int)Marshal.OffsetOf<NvI2cInfoV3>(nameof(NvI2cInfoV3.I2cDevAddress)));
        Assert.Equal(16, (int)Marshal.OffsetOf<NvI2cInfoV3>(nameof(NvI2cInfoV3.PbI2cRegAddress)));
        Assert.Equal(24, (int)Marshal.OffsetOf<NvI2cInfoV3>(nameof(NvI2cInfoV3.RegAddrSize)));
        Assert.Equal(32, (int)Marshal.OffsetOf<NvI2cInfoV3>(nameof(NvI2cInfoV3.PbData)));
        Assert.Equal(40, (int)Marshal.OffsetOf<NvI2cInfoV3>(nameof(NvI2cInfoV3.Size)));
        Assert.Equal(44, (int)Marshal.OffsetOf<NvI2cInfoV3>(nameof(NvI2cInfoV3.I2cSpeed)));
        Assert.Equal(48, (int)Marshal.OffsetOf<NvI2cInfoV3>(nameof(NvI2cInfoV3.I2cSpeedKhz)));
        Assert.Equal(52, (int)Marshal.OffsetOf<NvI2cInfoV3>(nameof(NvI2cInfoV3.PortId)));
        Assert.Equal(56, (int)Marshal.OffsetOf<NvI2cInfoV3>(nameof(NvI2cInfoV3.IsPortIdSet)));
    }

    [Fact]
    public void NvI2cInfoV3_targets_the_internal_rgb_port_with_shifted_address_and_never_a_ddc_port()
    {
        Assert.Equal(0x00030040u, NvI2cInfoV3.CurrentVersion);

        var info = NvI2cInfoV3.ForRgbPort(0x67);

        Assert.Equal(0x00030040u, info.Version);
        Assert.Equal((byte)0, info.IsDdcPort);
        Assert.Equal((byte)0xCE, info.I2cDevAddress);
        Assert.Equal((byte)(0x67 << 1), info.I2cDevAddress);
        Assert.Equal((byte)1, info.PortId);
        Assert.Equal(1u, info.IsPortIdSet);
        Assert.Equal(0xFFFFu, info.I2cSpeed);
    }

    [Fact]
    public void NvApi_pci_identifiers_unpack_device_and_subsystem_words()
    {
        var pci = NvApiPciIds.Unpack(0x2C0510DEu, 0x89F41043u);

        Assert.Equal((ushort)0x10DE, pci.VendorId);
        Assert.Equal((ushort)0x2C05, pci.DeviceId);
        Assert.Equal((ushort)0x1043, pci.SubVendorId);
        Assert.Equal((ushort)0x89F4, pci.SubDeviceId);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void NvApi_bus_enforces_the_address_firewall_before_touching_nvapi_and_reuses_one_lock_scope()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var bus = NvApiGpuI2cBus.CreateForTests();
        Span<byte> buffer = stackalloc byte[1];

        Assert.Throws<ArgumentOutOfRangeException>(() => bus.TryWriteRaw(0x50, [0x00]));
        Assert.Throws<ArgumentOutOfRangeException>(() => bus.TryWriteRegisterBlock(0x5F, [0x00], [0x01]));
        Assert.Throws<ArgumentOutOfRangeException>(() => bus.TryReadRegisterBlock(0x78, [0x00], new byte[1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => bus.TryReadRaw(0x08, new byte[1]));
        Assert.False(bus.TryReadRaw(0x67, buffer));

        AssertLockScopeIsReusableAndAllocationFree(bus);
    }

    // ------------------------------------------------------------------ Linux i2c-dev

    [Theory]
    [InlineData("NVIDIA i2c adapter 1 at 1:00.0", true)]
    [InlineData("NVIDIA i2c adapter 1 at 65:00.0", true)]
    [InlineData("NVIDIA i2c adapter 0 at 1:00.0", false)]
    [InlineData("NVIDIA i2c adapter 2 at 1:00.0", false)]
    [InlineData("NVIDIA i2c adapter 10 at 1:00.0", false)]
    [InlineData("NVIDIA i2c adapter 3 at 1:00.0", false)]
    [InlineData("NVIDIA GPU I2C adapter", false)]
    [InlineData("NVIDIA HDMI/DP,pcm=3", false)]
    [InlineData("AMDGPU DM aux hw bus 0", false)]
    [InlineData("DP-1", false)]
    [InlineData("card0-HDMI-A-1 DDC", false)]
    [InlineData("NVIDIA i2c adapter 1 dmdc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [SupportedOSPlatform("linux")]
    public void Only_the_internal_nvidia_port_1_adapter_is_accepted(string? adapterName, bool expected) =>
        Assert.Equal(expected, LinuxGpuI2cBusFactory.IsInternalNvidiaI2cAdapter(adapterName));

    [Theory]
    [InlineData("NVIDIA i2c adapter 1 at 1:00.0", true)]
    [InlineData("card0-HDMI-A-1", false)]
    [InlineData("DP-1", false)]
    [InlineData("AUX B/DDI B/PHY B", false)]
    [SupportedOSPlatform("linux")]
    public void Internal_gpu_rgb_adapter_filter_rejects_ddc_dp_hdmi_and_aux_names(string adapterName, bool expected) =>
        Assert.Equal(expected, LinuxGpuI2cBusFactory.IsInternalGpuRgbAdapter(adapterName));

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Linux_factory_discovers_only_internal_adapters_of_nvidia_display_controllers()
    {
        var sysfs = Path.Combine(_root, "sys");
        var dev = Path.Combine(_root, "dev");
        Directory.CreateDirectory(dev);

        // RTX 5070 Ti TUF: VGA controller with the internal port-1 adapter, a DDC port and DisplayPort aux adapters.
        var gpu = AddPciDevice(sysfs, "0000_01_00.0", 0x10DE, 0x2C05, 0x1043, 0x89F4, 0x030000);
        AddAdapter(gpu, "i2c-5", "NVIDIA i2c adapter 1 at 1:00.0");
        AddAdapter(gpu, "i2c-4", "NVIDIA i2c adapter 0 at 1:00.0");
        AddAdapter(gpu, "i2c-6", "NVIDIA i2c adapter 2 at 1:00.0");
        AddAdapter(Path.Combine(gpu, "drm", "card0"), "i2c-7", "NVIDIA HDMI/DP,pcm=3");
        AddAdapter(Path.Combine(gpu, "drm", "card0"), "i2c-8", "AMDGPU DM aux hw bus 0");
        AddAdapter(Path.Combine(gpu, "drm", "card0"), "i2c-9", "DP-1");

        // 3D controller class (0x0302xx) is a display controller as well.
        var compute = AddPciDevice(sysfs, "0000_02_00.0", 0x10DE, 0x2BB1, 0x10DE, 0x1234, 0x030200);
        AddAdapter(compute, "i2c-11", "NVIDIA i2c adapter 1 at 2:00.0");

        // HDMI audio function of the GPU and non-NVIDIA cards must never be considered.
        var audio = AddPciDevice(sysfs, "0000_01_00.1", 0x10DE, 0x22BE, 0x1043, 0x89F4, 0x040300);
        AddAdapter(audio, "i2c-20", "NVIDIA i2c adapter 1 at 1:00.1");
        var amd = AddPciDevice(sysfs, "0000_03_00.0", 0x1002, 0x744C, 0x1043, 0x0000, 0x030000);
        AddAdapter(amd, "i2c-21", "NVIDIA i2c adapter 1 at 3:00.0");

        var factory = new LinuxGpuI2cBusFactory(sysfs, dev);
        var adapters = factory.DiscoverAdapters().OrderBy(a => a.BusName, StringComparer.Ordinal).ToArray();

        Assert.Equal(["i2c-11", "i2c-5"], adapters.Select(a => a.BusName).ToArray());
        var tuf = Assert.Single(adapters, a => a.BusName == "i2c-5");
        Assert.Equal("NVIDIA i2c adapter 1 at 1:00.0", tuf.AdapterName);
        Assert.Equal("0000_01_00.0", tuf.PciAddress);
        Assert.Equal(new GpuPciIdentity(0x10DE, 0x2C05, 0x1043, 0x89F4), tuf.PciId);
        Assert.Equal(new GpuPciIdentity(0x10DE, 0x2BB1, 0x10DE, 0x1234), adapters.Single(a => a.BusName == "i2c-11").PciId);
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Linux_factory_without_sysfs_or_dev_nodes_is_unavailable_and_opens_nothing()
    {
        var sysfs = Path.Combine(_root, "sys");
        var dev = Path.Combine(_root, "dev");
        Directory.CreateDirectory(dev);
        var gpu = AddPciDevice(sysfs, "0000_01_00.0", 0x10DE, 0x2C05, 0x1043, 0x89F4, 0x030000);
        AddAdapter(gpu, "i2c-5", "NVIDIA i2c adapter 1 at 1:00.0");

        var factory = new LinuxGpuI2cBusFactory(sysfs, dev);
        var missing = new LinuxGpuI2cBusFactory(Path.Combine(_root, "nothing"), dev);

        Assert.Equal("i2c-dev", factory.BackendName);
        Assert.False(factory.IsAvailable);
        Assert.Empty(factory.OpenBuses());
        Assert.Empty(missing.DiscoverAdapters());
        Assert.False(missing.IsAvailable);
        Assert.Contains("i2c-dev", missing.DiagnosticStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Linux_bus_enforces_the_address_firewall_and_reuses_one_lock_scope()
    {
        var path = Path.Combine(_root, "i2c-test");
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(path, new byte[16]);
        using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        var pci = new GpuPciIdentity(0x10DE, 0x2C05, 0x1043, 0x89F4);
        using var bus = LinuxGpuI2cBus.CreateForTests(handle, pci);

        Assert.Equal(pci, bus.PciId);
        Assert.Throws<ArgumentOutOfRangeException>(() => bus.TryWriteRaw(0x50, [0x00]));
        Assert.Throws<ArgumentOutOfRangeException>(() => bus.TryWriteRegisterBlock(0x57, [0x00], [0x01]));
        Assert.Throws<ArgumentOutOfRangeException>(() => bus.TryReadRegisterBlock(0x10, [0x00], new byte[1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => bus.TryReadRaw(0x7F, new byte[1]));

        AssertLockScopeIsReusableAndAllocationFree(bus);
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Linux_i2c_structs_match_the_x64_kernel_layout()
    {
        if (!Environment.Is64BitProcess)
        {
            return;
        }

        Assert.Equal(16, LinuxGpuI2cBus.I2cMessageSize);
        Assert.Equal(16, LinuxGpuI2cBus.I2cRdwrIoctlDataSize);
    }

    [Theory]
    [InlineData(2, 2u, true)]
    [InlineData(1, 1u, true)]
    [InlineData(1, 2u, false)]
    [InlineData(0, 2u, false)]
    [InlineData(-1, 1u, false)]
    [InlineData(3, 2u, false)]
    [SupportedOSPlatform("linux")]
    public void Linux_rdwr_transfer_is_successful_only_when_every_message_completed(int ioctlResult, uint expected, bool success) =>
        Assert.Equal(success, LinuxGpuI2cBus.IsCompleteRdwrTransfer(ioctlResult, expected));

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Linux_bus_rejects_empty_register_address_after_the_firewall_check()
    {
        var path = Path.Combine(_root, "i2c-empty-reg");
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(path, new byte[16]);
        using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        using var bus = LinuxGpuI2cBus.CreateForTests(handle, new GpuPciIdentity(0x10DE, 0x2C05, 0x1043, 0x89F4));

        Assert.Throws<ArgumentOutOfRangeException>(() => bus.TryWriteRegisterBlock(0x50, [], [0x01]));
        Assert.Throws<ArgumentOutOfRangeException>(() => bus.TryReadRegisterBlock(0x50, [], new byte[1]));
        Assert.False(bus.TryWriteRegisterBlock(0x67, [], [0x01]));
        Assert.False(bus.TryReadRegisterBlock(0x67, [], new byte[1]));
    }

    // ------------------------------------------------------------------ udev

    [Fact]
    public void Udev_default_rules_describe_gpu_i2c_and_never_emit_nvidia_pci_vendor_rules()
    {
        var rules = UdevRulesGenerator.GenerateDefault();

        Assert.Contains("GPU", rules, StringComparison.Ordinal);
        Assert.Contains("KERNEL==\"i2c-[0-9]*\", SUBSYSTEM==\"i2c-dev\", TAG+=\"uaccess\"", rules, StringComparison.Ordinal);
        Assert.DoesNotContain("ATTRS{idVendor}==\"10de\"", rules, StringComparison.Ordinal);
    }

    [Fact]
    public void Udev_rules_skip_pci_gpu_manifests_even_when_they_exist()
    {
        DeviceManifest[] manifests =
        [
            new("nzxt-test", "NZXT Test", "NZXT", DeviceProtocolFamily.NzxtHue2, 0x1E71, 0x2009),
            new(
                "asus-5070ti-tuf",
                "ASUS GeForce RTX 5070 Ti TUF GAMING",
                "ASUS",
                DeviceProtocolFamily.AsusAuraGpuI2c,
                0x10DE,
                0x2C05,
                SubVendorId: 0x1043,
                SubDeviceId: 0x89F4,
                I2cAddress: 0x67),
        ];

        var rules = UdevRulesGenerator.GenerateForManifests(manifests);

        Assert.Contains("ATTRS{idVendor}==\"1e71\"", rules, StringComparison.Ordinal);
        Assert.DoesNotContain("10de", rules, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1043", rules, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ helpers

    private static void AssertLockScopeIsReusableAndAllocationFree(IGpuI2cBus bus)
    {
        var first = bus.AcquireBusLock();
        first.Dispose();
        var second = bus.AcquireBusLock();
        second.Dispose();
        Assert.Same(first, second);

        using (bus.AcquireBusLock())
        {
            using (bus.AcquireBusLock())
            {
            }
        }

        for (var i = 0; i < 100; i++)
        {
            using var warm = bus.AcquireBusLock();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            using var scope = bus.AcquireBusLock();
            using var nested = bus.AcquireBusLock();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static string AddPciDevice(string sysfsRoot, string address, int vendor, int device, int subVendor, int subDevice, int pciClass)
    {
        var dir = Path.Combine(sysfsRoot, "bus", "pci", "devices", address);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "vendor"), $"0x{vendor:x4}\n");
        File.WriteAllText(Path.Combine(dir, "device"), $"0x{device:x4}\n");
        File.WriteAllText(Path.Combine(dir, "subsystem_vendor"), $"0x{subVendor:x4}\n");
        File.WriteAllText(Path.Combine(dir, "subsystem_device"), $"0x{subDevice:x4}\n");
        File.WriteAllText(Path.Combine(dir, "class"), $"0x{pciClass:x6}\n");
        return dir;
    }

    private static void AddAdapter(string parentDirectory, string busName, string adapterName)
    {
        var dir = Path.Combine(parentDirectory, busName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "name"), adapterName + "\n");
    }
}
