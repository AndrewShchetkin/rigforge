using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Win32.SafeHandles;
using RigForge.Application;
using RigForge.Application.AioDisplay;
using RigForge.Application.Devices;
using RigForge.Application.Effects;
using RigForge.Application.Profiles;
using RigForge.Application.Telemetry;
using RigForge.Application.Validation;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Host.Daemon;
using RigForge.Infrastructure.CrossPlatform.Manifests;
using RigForge.Infrastructure.CrossPlatform.Platform;
using RigForge.Infrastructure.CrossPlatform.Smbus;
using RigForge.Infrastructure.Linux.Smbus;
using RigForge.Infrastructure.Linux.Udev;
using RigForge.Tools.HardwareScanner;

namespace RigForge.UnitTests.Platform;

public sealed class LinuxParityAndIntegrationTests
{
    [Fact]
    [SupportedOSPlatform("linux")]
    public void Reentrant_smbus_lock_accepts_nested_transfers()
    {
        var path = Path.GetTempFileName();
        var handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        try
        {
            using var controller = LinuxI2cSmbusController.CreateForTests(handle);
            using (controller.AcquireBusLock())
            {
                using (controller.AcquireBusLock())
                {
                    Assert.False(controller.TryWriteByteData(0x60, 0x01, 0x10));
                    Assert.False(controller.TryReadWordData(0x61, 0x02, out var word));
                    Assert.Equal((ushort)0, word);
                }

                Assert.False(controller.TryWriteByteData(0x62, 0x03, 0x20));
            }

            Assert.False(controller.TryReadWordData(0x63, 0x04, out _));
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // The handle is released by the controller. A sharing glitch must not hide a lock failure.
            }
        }
    }

    [Fact]
    public void Spd_eeprom_parser_reads_ddr4_and_ddr5_part_numbers_without_smbus()
    {
        var ddr4 = new byte[512];
        "KF556C40BBAK2-64".CopyAscii(ddr4, SpdEepromParser.Ddr4PartNumberOffset, SpdEepromParser.Ddr4PartNumberLength);
        Assert.Equal("KF556C40BBAK2-64", SpdEepromParser.ReadDdr4PartNumber(ddr4));
        Assert.Equal("KF556C40BBAK2-64", SpdEepromParser.ReadPartNumber(ddr4));
        var fury = SpdEepromParser.ToStick("0-0050", ddr4);
        Assert.NotNull(fury);
        Assert.True(fury.IsKingstonFuryDdr5);

        var ddr5 = new byte[1024];
        "CMH32GX5M2B6000C30".CopyAscii(ddr5, SpdEepromParser.Ddr5PartNumberOffset, SpdEepromParser.Ddr5PartNumberLength);
        Assert.Equal("CMH32GX5M2B6000C30", SpdEepromParser.ReadDdr5PartNumber(ddr5));
        Assert.Equal("CMH32GX5M2B6000C30", SpdEepromParser.ReadPartNumber(ddr5));
        var corsair = SpdEepromParser.ToStick("1-0051", ddr5);
        Assert.NotNull(corsair);
        Assert.True(corsair.IsCorsairDram);
        Assert.False(corsair.IsKingstonFuryDdr5);

        Assert.Null(SpdEepromParser.ReadPartNumber([]));
        Assert.True(SpdEepromParser.IsSpdDeviceName("i2c-1-0050"));
        Assert.True(SpdEepromParser.IsSpdDeviceName("0-0057"));
        Assert.False(SpdEepromParser.IsSpdDeviceName("0-0058"));
        Assert.False(SpdEepromParser.IsSpdDeviceName("0-004f"));
    }

    [Fact]
    public void Chipset_adapter_filter_keeps_smbus_and_rejects_display_buses()
    {
        Assert.True(LinuxChipsetSmbus.IsAdapter("SMBus PIIX4 adapter port 0 at 0b00"));
        Assert.True(LinuxChipsetSmbus.IsAdapter("SMBus I801 adapter at 0400"));
        Assert.True(LinuxChipsetSmbus.IsAdapter("SMBus adapter"));
        Assert.True(LinuxChipsetSmbus.IsAdapter("Synopsys DesignWare I2C adapter"));
        Assert.True(LinuxChipsetSmbus.IsAdapter("AMD KERNCZ SMBus"));
        Assert.True(LinuxChipsetSmbus.IsAdapter("NCT6775 SMBus adapter"));
        Assert.False(LinuxChipsetSmbus.IsAdapter("NVIDIA i2c adapter 1 at 1:00.0"));
        Assert.False(LinuxChipsetSmbus.IsAdapter("AMDGPU DM i2c"));
        Assert.False(LinuxChipsetSmbus.IsAdapter("AMDGPU DM aux hw bus 0"));
        Assert.False(LinuxChipsetSmbus.IsAdapter("NCT6775 hwmon"));
    }

    [Fact]
    public void Smbus_policy_requires_a_matching_module_before_any_bus_probe()
    {
        var readable = new FakeSmbusFactory(available: true, canReadSmbios: true);
        Assert.False(SmbusProbePolicy.AllowsBusProbe(readable, requireSmbiosMatch: true, matchedCount: 0));
        Assert.True(SmbusProbePolicy.AllowsBusProbe(readable, requireSmbiosMatch: false, matchedCount: 0));
        Assert.True(SmbusProbePolicy.AllowsBusProbe(readable, requireSmbiosMatch: true, matchedCount: 2));

        // Unreadable DMI must not bypass RequireSmbiosMatch. A positive count is a module already
        // identified from DMI or from a cached sysfs SPD EEPROM (/sys/bus/i2c/devices/*-005[0-7]/eeprom), udev, or EDAC.
        var unread = new FakeSmbusFactory(available: true, canReadSmbios: false);
        Assert.False(SmbusProbePolicy.AllowsBusProbe(unread, requireSmbiosMatch: true, matchedCount: 0));
        Assert.True(SmbusProbePolicy.AllowsBusProbe(unread, requireSmbiosMatch: true, matchedCount: 1));
        Assert.True(SmbusProbePolicy.AllowsBusProbe(unread, requireSmbiosMatch: false, matchedCount: 0));

        var unavailable = new FakeSmbusFactory(available: false, canReadSmbios: false);
        Assert.False(SmbusProbePolicy.AllowsBusProbe(unavailable, requireSmbiosMatch: true, matchedCount: 0));
        Assert.True(SmbusProbePolicy.AllowsBusProbe(unavailable, requireSmbiosMatch: false, matchedCount: 0));
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Smbus_transfers_fail_closed_when_the_device_lock_cannot_be_taken()
    {
        var path = Path.GetTempFileName();
        var handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        handle.Dispose();
        try
        {
            using var controller = LinuxI2cSmbusController.CreateForTests(handle, useCrossProcessLock: true);
            Assert.False(controller.TryWriteByteData(0x60, 0x08, 0x53));
            Assert.False(controller.TryReadWordData(0x70, 0x00, out var word));
            Assert.Equal((ushort)0, word);
            var error = Assert.Throws<IOException>(() => controller.AcquireBusLock());
            Assert.Contains("unlocked", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // The handle is already closed. A sharing glitch must not hide a lock failure.
            }
        }
    }

    [Fact]
    public async Task Each_lcd_uses_its_display_override()
    {
        var screenA = new RecordingLcd("screen-a", new LcdInfo(240, 240, true, 30f));
        var screenB = new RecordingLcd("screen-b", new LcdInfo(320, 320, true, 30f));
        var devices = new DeviceManager([new StaticProbe(screenA, screenB)], NullLogger<DeviceManager>.Instance);
        await devices.ScanAsync(TestContext.Current.CancellationToken);

        var registry = new EffectRegistry(new NullScreenCapturer());
        var profiles = new ProfileService(
            new InMemoryProfileStore(),
            new RigProfileValidator(registry),
            NullLogger<ProfileService>.Instance,
            registry);
        var updated = await profiles.ReplaceProfileAsync(
            new RigProfile
            {
                Lcd = new LcdSettings { Mode = LcdMode.BuiltInLiquid, Brightness = 40, FramesPerSecond = 20f },
                Displays = new Dictionary<string, LcdSettings>(StringComparer.Ordinal)
                {
                    ["screen-b"] = new LcdSettings { Mode = LcdMode.Dashboard, Brightness = 90, FramesPerSecond = 20f },
                },
            },
            TestContext.Current.CancellationToken);
        Assert.True(updated.IsValid, string.Join("; ", updated.Errors));

        var telemetry = new TelemetryService(
            devices,
            Options.Create(new TelemetryOptions()),
            TimeProvider.System,
            NullLogger<TelemetryService>.Instance);
        var engine = new LcdRenderEngine(
            devices,
            profiles,
            telemetry,
            Options.Create(new LcdOptions { MaxFramesPerSecond = 20f, KeepAliveInterval = TimeSpan.FromHours(1) }),
            TimeProvider.System,
            NullLogger<LcdRenderEngine>.Instance);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = engine.RunAsync(cts.Token);
        await screenB.WaitForPushesAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal([40], screenA.Brightness);
        Assert.Equal([90], screenB.Brightness);
        Assert.Equal(0, screenA.PushCount);
        Assert.True(screenA.BuiltInCount >= 1);
        Assert.True(screenB.PushCount >= 1);
        Assert.Equal(screenB.Width * screenB.Height * 4, screenB.LastFrameLength);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        await devices.DisposeAsync();
    }

    [Fact]
    public void Manifest_catalog_and_udev_rules_cover_the_same_usb_families()
    {
        var catalog = new JsonDeviceManifestCatalog(string.Empty);
        DeviceProtocolFamily[] families =
        [
            DeviceProtocolFamily.NzxtKraken,
            DeviceProtocolFamily.NzxtKrakenX3,
            DeviceProtocolFamily.NzxtHue2,
            DeviceProtocolFamily.AsusAuraUsb,
            DeviceProtocolFamily.CorsairLightingNode,
            DeviceProtocolFamily.CorsairCommanderCore,
            DeviceProtocolFamily.MsiMysticLightUsb,
            DeviceProtocolFamily.GigabyteFusion2Usb,
            DeviceProtocolFamily.AsRockPolychromeUsb,
            DeviceProtocolFamily.LianLiUniHub,
            DeviceProtocolFamily.CoolerMasterArgb,
            DeviceProtocolFamily.LogitechHidPp,
            DeviceProtocolFamily.RazerChromaHid,
        ];

        foreach (var family in families)
        {
            Assert.NotEmpty(catalog.ForProtocol(family));
        }

        Assert.NotNull(catalog.FindUsb(0x1E71, 0x2007));
        Assert.NotNull(catalog.FindUsb(0x1B1C, 0x0C1C));
        Assert.NotNull(catalog.FindUsb(0x1462, 0x7C75));
        Assert.NotNull(catalog.FindUsb(0x0DB0, 0x7C75));
        Assert.NotNull(catalog.FindUsb(0x048D, 0x8297));
        Assert.NotNull(catalog.FindUsb(0x26CE, 0x01A2));
        Assert.NotNull(catalog.FindUsb(0x0CF2, 0xA100));
        Assert.NotNull(catalog.FindUsb(0x2516, 0x0173));
        Assert.NotNull(catalog.FindUsb(0x046D, 0xC092));
        Assert.NotNull(catalog.FindUsb(0x1532, 0x0F1F));

        var generated = Normalize(UdevRulesGenerator.GenerateDefault());
        var temp = Path.Combine(Path.GetTempPath(), $"rigforge-udev-{Guid.NewGuid():N}.rules");
        try
        {
            Assert.Equal(0, UdevCommand.Run(new FileInfo(temp)));
            Assert.Equal(generated, Normalize(File.ReadAllText(temp)));
        }
        finally
        {
            File.Delete(temp);
        }

        var committed = Normalize(File.ReadAllText(FindRepoFile(Path.Combine("deploy", "linux", "60-rigforge.rules"))));
        Assert.Equal(generated, committed);
        Assert.Contains("ATTRS{idVendor}==\"1e71\"", generated, StringComparison.Ordinal);
        Assert.Contains("ATTRS{idVendor}==\"0b05\"", generated, StringComparison.Ordinal);
        Assert.Contains("ATTRS{idVendor}==\"1b1c\"", generated, StringComparison.Ordinal);
        Assert.Contains("ATTRS{idVendor}==\"1462\"", generated, StringComparison.Ordinal);
        Assert.Contains("ATTRS{idVendor}==\"0db0\"", generated, StringComparison.Ordinal);
        Assert.Contains("5702", generated, StringComparison.Ordinal);
        Assert.Contains("01a2", generated, StringComparison.Ordinal);
        Assert.Contains("a100", generated, StringComparison.Ordinal);
        Assert.Contains("0173", generated, StringComparison.Ordinal);
        Assert.Contains("ATTRS{idVendor}==\"046d\"", generated, StringComparison.Ordinal);
        Assert.Contains("ATTRS{idVendor}==\"1532\"", generated, StringComparison.Ordinal);
        Assert.Contains("ENV{ID_USB_INTERFACE_NUM}==\"01\"", generated, StringComparison.Ordinal);
        Assert.Contains("SUBSYSTEM==\"i2c-dev\"", generated, StringComparison.Ordinal);
        Assert.Contains("SUBSYSTEM==\"dmi\", KERNEL==\"id\"", generated, StringComparison.Ordinal);
        Assert.Contains("/sys/firmware/dmi/tables/DMI", generated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Device_manager_drops_unmanaged_duplicates_and_keeps_other_unmanaged_devices()
    {
        var supported = new TaggedDevice("kraken", DeviceSupportStatus.Supported, "1E71:3008");
        var duplicate = new TaggedDevice("unmanaged-kraken", DeviceSupportStatus.Unmanaged, "1e71:3008");
        var other = new TaggedDevice("unmanaged-board", DeviceSupportStatus.Unmanaged, "0x3434:0x0001");
        var manager = new DeviceManager(
            [new StaticProbe(supported), new StaticProbe(duplicate, other)],
            NullLogger<DeviceManager>.Instance);

        await manager.ScanAsync(TestContext.Current.CancellationToken);

        Assert.Contains(manager.Devices, device => device.Id == "kraken");
        Assert.Contains(manager.Devices, device => device.Id == "unmanaged-board");
        Assert.DoesNotContain(manager.Devices, device => device.Id == "unmanaged-kraken");
        Assert.True(duplicate.Disposed);
        Assert.False(other.Disposed);
        Assert.False(supported.Disposed);

        await manager.DisposeAsync();
    }

    [Fact]
    public async Task Daemon_provider_resolves_probes_and_the_effect_registry()
    {
        var provider = DaemonServiceCollectionExtensions.BuildDaemonProvider();
        try
        {
            var probes = (IEnumerable<IDeviceProbe>)provider.GetService(typeof(IEnumerable<IDeviceProbe>))!;
            var names = probes.Select(probe => probe.Name).ToArray();
            Assert.NotEmpty(names);
            string[] expected =
            [
                "NZXT",
                "ASUS",
                "Kingston",
                "NVIDIA",
                "Corsair Lighting",
                "MSI",
                "Gigabyte",
                "ASRock",
                "Lian Li",
                "Cooler Master",
                "QMK",
                "Logitech",
                "Razer",
                "ENE",
                "Corsair RGB DRAM",
                "OpenRGB",
                "Unmanaged",
            ];

            foreach (var fragment in expected)
            {
                Assert.Contains(names, name => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
            }

            if (OperatingSystem.IsWindows())
            {
                Assert.Contains(names, name => name.Contains("Windows", StringComparison.OrdinalIgnoreCase));
            }
            else if (OperatingSystem.IsLinux())
            {
                Assert.Contains(names, name => name.Contains("hwmon", StringComparison.OrdinalIgnoreCase));
            }

            var registry = provider.GetService(typeof(EffectRegistry)) as EffectRegistry;
            Assert.NotNull(registry);
            Assert.True(registry.Contains("rainbow-wave"));
            Assert.NotNull(registry.Create("rainbow-wave"));
        }
        finally
        {
            if (provider is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (provider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim() + "\n";

    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relative);
    }

    private sealed class FakeSmbusFactory(bool available, bool canReadSmbios) : ISmbusControllerFactory
    {
        public string BackendName => "fake";

        public bool IsAvailable => available;

        public bool CanReadSmbios => canReadSmbios;

        public string DiagnosticStatus => "fake";

        public IReadOnlyList<SmbiosMemoryStick> GetInstalledMemory() => [];

        public IReadOnlyList<ISmbusController> OpenControllers() => [];
    }

    private sealed class TaggedDevice(string id, DeviceSupportStatus status, string vidPid) : IHardwareDevice
    {
        public string Id => id;

        public string Name => id;

        public string Vendor => "Test";

        public DeviceCapabilities Capabilities =>
            status == DeviceSupportStatus.Supported ? DeviceCapabilities.Rgb : DeviceCapabilities.None;

        public DeviceSupportStatus SupportStatus => status;

        public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string> { ["vidPid"] = vidPid };

        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingLcd(string id, LcdInfo info) : IAioLcdDevice
    {
        private readonly Lock _gate = new();
        private readonly List<TaskCompletionSource> _waiters = [];

        public string Id => id;

        public string Name => id;

        public string Vendor => "Test";

        public DeviceCapabilities Capabilities => DeviceCapabilities.Lcd;

        public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>();

        public LcdInfo Lcd => info;

        public int Width => info.Width;

        public int Height => info.Height;

        public List<int> Brightness { get; } = [];

        public int BuiltInCount { get; private set; }

        public int PushCount { get; private set; }

        public int LastFrameLength { get; private set; }

        public ValueTask PushFrameAsync(ReadOnlyMemory<byte> rgba, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                LastFrameLength = rgba.Length;
                PushCount++;
                foreach (var waiter in _waiters)
                {
                    waiter.TrySetResult();
                }
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask SetBrightnessAsync(int percent, CancellationToken cancellationToken)
        {
            Brightness.Add(percent);
            return ValueTask.CompletedTask;
        }

        public ValueTask ShowBuiltInScreenAsync(CancellationToken cancellationToken)
        {
            BuiltInCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public async Task WaitForPushesAsync(int target, CancellationToken cancellationToken)
        {
            while (true)
            {
                TaskCompletionSource waiter;
                lock (_gate)
                {
                    if (PushCount >= target)
                    {
                        return;
                    }

                    waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _waiters.Add(waiter);
                }

                await waiter.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }
}

file static class SpdAscii
{
    public static void CopyAscii(this string text, byte[] buffer, int offset, int length)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        bytes.CopyTo(buffer.AsSpan(offset));
        for (var i = bytes.Length; i < length; i++)
        {
            buffer[offset + i] = 0x20;
        }
    }
}
