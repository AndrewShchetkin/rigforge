using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RigForge.Application;
using RigForge.Application.Devices;
using RigForge.Application.Diagnostics;
using RigForge.Application.Effects;
using RigForge.Application.Effects.Library;
using RigForge.Application.Profiles;
using RigForge.Application.Telemetry;
using RigForge.Application.Validation;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Platform;

namespace RigForge.UnitTests.Foundation;

public sealed class FoundationContractsAndPerfTests
{
    private static readonly byte[] Red = [0xFF, 0x00, 0x00];
    private static readonly byte[] Cyan = [0x00, 0xF5, 0xFF];
    private static readonly byte[] Dim = [0x0A, 0x10, 0x18];

    private static readonly EffectDescriptor ProbeDescriptor = new(
        "probe",
        "Probe",
        "Exercises the extended parameter schema.",
        IsAnimated: false,
        Parameters:
        [
            EffectParameterDefinition.Boolean("mirror", "Mirror", true),
            EffectParameterDefinition.IntRange("count", "Count", 2, 0, 8, unit: "leds"),
            EffectParameterDefinition.Select("mode", "Mode", "pulse", [new EffectSelectOption("pulse", "Pulse"), new EffectSelectOption("solid", "Solid")]),
            EffectParameterDefinition.FloatRange("gain", "Gain", 0.5, 0, 1, 0.1),
        ],
        Category: EffectCategory.Reactive,
        UsesAudio: true);

    private readonly EffectRegistry _registry = new(new NullScreenCapturer(), new ProbeCatalog());
    private readonly RigProfileValidator _validator;

    public FoundationContractsAndPerfTests()
    {
        _validator = new RigProfileValidator(_registry);
    }

    [Fact]
    public void Effect_settings_round_trip_enabled_and_legacy_json_defaults_to_on()
    {
        var disabled = new EffectSettings
        {
            EffectId = "solid",
            Enabled = false,
            Brightness = 0.25f,
            TargetFps = 24,
        };

        var json = JsonSerializer.Serialize(disabled);
        using (var document = JsonDocument.Parse(json))
        {
            var root = document.RootElement;
            Assert.False(root.GetProperty("enabled").GetBoolean());
            Assert.Equal("solid", root.GetProperty("effectId").GetString());
            Assert.Equal(0.25f, root.GetProperty("brightness").GetSingle());
            Assert.Equal(24, root.GetProperty("targetFps").GetInt32());
            Assert.Equal(JsonValueKind.Object, root.GetProperty("parameters").ValueKind);
        }

        var roundTripped = JsonSerializer.Deserialize<EffectSettings>(json);
        Assert.NotNull(roundTripped);
        Assert.False(roundTripped.Enabled);
        Assert.Equal(0.25f, roundTripped.Brightness);
        Assert.Equal("solid", roundTripped.EffectId);

        var omitted = JsonSerializer.Deserialize<EffectSettings>("""{"effectId":"gradient","brightness":0.4,"targetFps":30}""");
        Assert.NotNull(omitted);
        Assert.True(omitted.Enabled);
        Assert.Equal(0.4f, omitted.Brightness);
        Assert.Equal(30, omitted.TargetFps);

        var caseInsensitive = JsonSerializer.Deserialize<EffectSettings>("""{"effectId":"solid","EnAbLeD":false}""");
        Assert.NotNull(caseInsensitive);
        Assert.False(caseInsensitive.Enabled);
        Assert.Equal(1f, caseInsensitive.Brightness);
    }

    [Fact]
    public void Extended_effect_parameters_validate_and_parse_without_allocating_on_read()
    {
        Assert.True(IsValid(ProbeSettings(("mirror", false), ("count", 4), ("mode", "solid"), ("gain", 0.25))));
        Assert.False(IsValid(ProbeSettings(("count", 9))));
        Assert.False(IsValid(ProbeSettings(("count", -1))));
        Assert.False(IsValid(ProbeSettings(("count", 1.5))));
        Assert.False(IsValid(ProbeSettings(("mode", "nope"))));
        Assert.False(IsValid(ProbeSettings(("mode", "Pulse"))));
        Assert.False(IsValid(ProbeSettings(("mirror", 1))));

        var settings = ProbeSettings(("mirror", false), ("count", 4), ("mode", "solid"), ("gain", 0.25));
        var parsed = EffectParameterValues.Parse(settings, ProbeDescriptor);
        var bands = new float[] { 0.1f, 0.2f };
        var context = new EffectFrameContext(
            8,
            4,
            1.5,
            settings,
            [],
            new TelemetrySnapshot(DateTimeOffset.UnixEpoch, []),
            AudioLevel: 0.5f,
            AudioBands: bands,
            Numbers: parsed.Numbers,
            Booleans: parsed.Booleans,
            Strings: parsed.Strings);

        Assert.Equal(0.25f, context.GetFloat("gain"));
        Assert.Equal(4f, context.GetFloat("count"));
        Assert.Equal(4, context.GetInt("count"));
        Assert.False(context.GetBool("mirror"));
        Assert.Equal("solid", context.GetString("mode"));
        Assert.Equal(7f, context.GetFloat("missing", 7f));
        Assert.Equal(3, context.GetInt("missing", 3));
        Assert.True(context.GetBool("missing", true));
        Assert.Equal("fallback", context.GetString("missing", "fallback"));
        Assert.Equal(0.5f, context.AudioLevel);
        Assert.Equal(0.1f, context.Band(0));
        Assert.Equal(0.2f, context.Band(1));
        Assert.Equal(0f, context.Band(8));

        _ = context.GetFloat("gain");
        _ = context.GetInt("count");
        _ = context.GetBool("mirror");
        _ = context.GetString("mode");
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            _ = context.GetFloat("gain");
            _ = context.GetInt("count");
            _ = context.GetBool("mirror");
            _ = context.GetString("mode");
            _ = context.Band(0);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Support_status_follows_capabilities()
    {
        IHardwareDevice rgb = new FakeRgbDevice("led", Zone("z"));
        Assert.Equal(DeviceSupportStatus.Supported, rgb.SupportStatus);
        Assert.Null(rgb.StatusReason);
        IHardwareDevice sensors = new SensorOnlyDevice();
        IHardwareDevice unmanaged = new UnmanagedDevice();
        Assert.Equal(DeviceSupportStatus.TelemetryOnly, sensors.SupportStatus);
        Assert.Equal(DeviceSupportStatus.Unmanaged, unmanaged.SupportStatus);
        Assert.True(Enum.IsDefined(DeviceProtocolFamily.NzxtKrakenX3));
        Assert.True(Enum.IsDefined(DeviceProtocolFamily.OpenRgbBridge));
        Assert.True(Enum.IsDefined(DeviceProtocolFamily.DetectedUnmanaged));
    }

    [Fact]
    public async Task Master_power_blacks_out_and_identify_blinks_then_restores_the_effect()
    {
        var device = new FakeRgbDevice("strip", Zone("a"), Zone("b"));
        var devices = new DeviceManager([new StaticProbe(device)], NullLogger<DeviceManager>.Instance);
        await devices.ScanAsync(TestContext.Current.CancellationToken);
        var profiles = new ProfileService(new InMemoryProfileStore(), _validator, NullLogger<ProfileService>.Instance, _registry);
        var updated = await profiles.UpdateEffectAsync(
            EffectSettingsFactory.Create(SolidColorEffect.EffectId, ("colors", new[] { "#FF0000" })),
            TestContext.Current.CancellationToken);
        Assert.True(updated.IsValid);

        var engine = CreateEngine(devices, profiles);
        engine.PreviewRequested = true;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(8));
        var loop = engine.RunAsync(cts.Token);
        try
        {
            await WaitUntilAsync(() => ZoneColor(engine, "a").AsSpan().SequenceEqual(Red) && ZoneColor(engine, "b").AsSpan().SequenceEqual(Red), cts.Token);

            var poweredOff = await profiles.UpdateEffectAsync(profiles.Current.Effect with { Enabled = false }, cts.Token);
            Assert.True(poweredOff.IsValid);
            Assert.Equal(1f, profiles.Current.Effect.Brightness);
            Assert.False(profiles.Current.Effect.Enabled);

            await WaitUntilAsync(
                () => engine.CurrentRenderMode == EffectRenderMode.Blackout
                    && engine.FramesPerSecond == 0
                    && engine.AverageFrameMicroseconds == 0
                    && ZoneColor(engine, "a").AsSpan().SequenceEqual(new byte[3])
                    && ZoneColor(engine, "b").AsSpan().SequenceEqual(new byte[3]),
                cts.Token);

            var restored = await profiles.UpdateEffectAsync(profiles.Current.Effect with { Enabled = true }, cts.Token);
            Assert.True(restored.IsValid);
            await WaitUntilAsync(() => ZoneColor(engine, "a").AsSpan().SequenceEqual(Red), cts.Token);

            engine.TriggerIdentify(device.Id, "a", TimeSpan.FromMilliseconds(700));
            await WaitUntilAsync(
                () => IsBlink(ZoneColor(engine, "a")) && ZoneColor(engine, "b").AsSpan().SequenceEqual(Red),
                cts.Token);
            await WaitUntilAsync(
                () => engine.CurrentRenderMode == EffectRenderMode.Sleeping
                    && ZoneColor(engine, "a").AsSpan().SequenceEqual(Red)
                    && ZoneColor(engine, "b").AsSpan().SequenceEqual(Red),
                cts.Token);

            engine.TriggerIdentify(device.Id, duration: TimeSpan.FromMilliseconds(700));
            await WaitUntilAsync(() => IsBlink(ZoneColor(engine, "a")) && IsBlink(ZoneColor(engine, "b")), cts.Token);
            await WaitUntilAsync(
                () => ZoneColor(engine, "a").AsSpan().SequenceEqual(Red) && ZoneColor(engine, "b").AsSpan().SequenceEqual(Red),
                cts.Token);
        }
        finally
        {
            await cts.CancelAsync();
            await loop;
        }
    }

    [Fact]
    public async Task Performance_monitor_reports_memory_gc_leds_and_render_mode()
    {
        var devices = new DeviceManager(
            [new StaticProbe(new FakeRgbDevice("led", Zone("z")))],
            NullLogger<DeviceManager>.Instance);
        await devices.ScanAsync(TestContext.Current.CancellationToken);
        var profiles = new ProfileService(new InMemoryProfileStore(), _validator, NullLogger<ProfileService>.Instance, _registry);
        await profiles.UpdateEffectAsync(
            EffectSettingsFactory.Create(SolidColorEffect.EffectId, ("colors", new[] { "#FF0000" })),
            TestContext.Current.CancellationToken);

        var engine = CreateEngine(devices, profiles);
        engine.PreviewRequested = true;
        var monitor = new PerformanceMonitor(engine);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        var loop = engine.RunAsync(cts.Token);
        try
        {
            await WaitUntilAsync(
                () => engine.LatestPreview.Count > 0
                    && engine.TotalControlledLeds > 0
                    && engine.CurrentRenderMode == EffectRenderMode.Sleeping,
                cts.Token);
            var snapshot = monitor.Capture();

            Assert.InRange(snapshot.CpuPercent, 0d, 100d);
            Assert.True(snapshot.PrivateBytes > 0);
            Assert.True(snapshot.WorkingSetBytes > 0);
            Assert.True(snapshot.ManagedHeapBytes > 0);
            Assert.True(snapshot.Gen0Collections >= 0);
            Assert.True(snapshot.Gen1Collections >= 0);
            Assert.True(snapshot.Gen2Collections >= 0);
            Assert.Equal(engine.ActiveRgbDeviceCount, snapshot.ActiveRgbDevices);
            Assert.Equal(engine.TotalControlledLeds, snapshot.TotalControlledLeds);
            Assert.Equal(engine.CurrentRenderMode, snapshot.RenderMode);
            Assert.True(snapshot.ActiveRgbDevices >= 1);
            Assert.True(snapshot.TotalControlledLeds >= 1);
            Assert.True(snapshot.FrameTimeMicroseconds > 0);
            Assert.Equal(EffectRenderMode.Sleeping, snapshot.RenderMode);
        }
        finally
        {
            await cts.CancelAsync();
            await loop;
        }
    }

    [Fact]
    public async Task Profile_import_replaces_the_document_and_displays_are_validated_and_salvaged()
    {
        var store = new InMemoryProfileStore();
        var service = new ProfileService(store, _validator, NullLogger<ProfileService>.Instance, _registry);
        var original = await service.UpdateEffectAsync(
            EffectSettingsFactory.Create(SolidColorEffect.EffectId, ("colors", new[] { "#00FF00" })),
            TestContext.Current.CancellationToken);
        Assert.True(original.IsValid);

        var rejected = await service.ReplaceProfileAsync(
            new RigProfile { Effect = new EffectSettings { EffectId = "nope" } },
            TestContext.Current.CancellationToken);
        Assert.False(rejected.IsValid);
        Assert.Equal(SolidColorEffect.EffectId, service.Current.Effect.EffectId);

        var imported = await service.ReplaceProfileAsync(
            new RigProfile
            {
                SchemaVersion = 1,
                Effect = new EffectSettings { EffectId = SolidColorEffect.EffectId, Enabled = false, Brightness = 0.4f },
                Displays = new Dictionary<string, LcdSettings>(StringComparer.Ordinal)
                {
                    ["kraken"] = new LcdSettings { Brightness = 15 },
                },
            },
            TestContext.Current.CancellationToken);
        Assert.True(imported.IsValid);
        Assert.Equal(RigProfile.CurrentSchemaVersion, service.Current.SchemaVersion);
        Assert.False(service.Current.Effect.Enabled);
        Assert.Equal(0.4f, service.Current.Effect.Brightness);
        Assert.True(service.Current.Effect.Parameters.ContainsKey("colors"));
        Assert.Equal(15, service.Current.Displays["kraken"].Brightness);

        var missingDisplays = await service.ReplaceProfileAsync(
            new RigProfile { Displays = null!, Effect = new EffectSettings { EffectId = SolidColorEffect.EffectId } },
            TestContext.Current.CancellationToken);
        Assert.True(missingDisplays.IsValid);
        Assert.NotNull(service.Current.Displays);
        Assert.Empty(service.Current.Displays);

        var upserted = await service.UpsertDisplayAsync("kraken", new LcdSettings { Brightness = 33 }, TestContext.Current.CancellationToken);
        Assert.True(upserted.IsValid);
        var second = await service.UpsertDisplayAsync("hue", new LcdSettings { Brightness = 44 }, TestContext.Current.CancellationToken);
        Assert.True(second.IsValid);
        Assert.Equal(2, service.Current.Displays.Count);
        Assert.Equal(33, service.Current.Displays["kraken"].Brightness);

        var removed = await service.RemoveDisplayAsync("kraken", TestContext.Current.CancellationToken);
        Assert.True(removed.IsValid);
        Assert.False(service.Current.Displays.ContainsKey("kraken"));
        Assert.Equal(44, service.Current.Displays["hue"].Brightness);

        Assert.False(_validator.Validate(new RigProfile { Displays = null! }).IsValid);
        Assert.False(_validator.Validate(new RigProfile
        {
            Displays = new Dictionary<string, LcdSettings> { [""] = new LcdSettings() },
        }).IsValid);
        Assert.False(_validator.Validate(new RigProfile
        {
            Displays = new Dictionary<string, LcdSettings> { [new string('x', 257)] = new LcdSettings() },
        }).IsValid);
        Assert.False(_validator.Validate(new RigProfile
        {
            Displays = new Dictionary<string, LcdSettings> { ["ok"] = new LcdSettings { Brightness = 101 } },
        }).IsValid);

        var sixteen = new Dictionary<string, LcdSettings>(StringComparer.Ordinal);
        for (var i = 0; i < 16; i++)
        {
            sixteen[$"d{i}"] = new LcdSettings();
        }

        Assert.True(_validator.Validate(new RigProfile { Displays = sixteen }).IsValid);
        sixteen["overflow"] = new LcdSettings();
        Assert.False(_validator.Validate(new RigProfile { Displays = sixteen }).IsValid);

        var salvageStore = new InMemoryProfileStore
        {
            Saved = new RigProfile
            {
                Effect = new EffectSettings { EffectId = "nope" },
                Displays = new Dictionary<string, LcdSettings>(StringComparer.Ordinal)
                {
                    ["good"] = new LcdSettings { Brightness = 12 },
                    ["bad"] = new LcdSettings { Brightness = 500 },
                },
            },
        };
        var salvage = new ProfileService(salvageStore, _validator, NullLogger<ProfileService>.Instance, _registry);
        await salvage.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(12, salvage.Current.Displays["good"].Brightness);
        Assert.False(salvage.Current.Displays.ContainsKey("bad"));
        Assert.Equal(new EffectSettings().EffectId, salvage.Current.Effect.EffectId);

        var legacyStore = new InMemoryProfileStore
        {
            Saved = new RigProfile { Displays = null!, Canvas = new VirtualCanvas { Width = 400 } },
        };
        var legacy = new ProfileService(legacyStore, _validator, NullLogger<ProfileService>.Instance, _registry);
        await legacy.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, legacyStore.BackupCount);
        Assert.NotNull(legacy.Current.Displays);
        Assert.Empty(legacy.Current.Displays);
        Assert.Equal(400, legacy.Current.Canvas.Width);
    }

    private bool IsValid(EffectSettings effect) => _validator.Validate(new RigProfile { Effect = effect }).IsValid;

    private static EffectSettings ProbeSettings(params (string Key, object Value)[] parameters) =>
        EffectSettingsFactory.Create(ProbeDescriptor.Id, parameters);

    private static RgbZone Zone(string id) => new()
    {
        Id = id,
        Name = id,
        Topology = LedTopology.Linear,
        Leds = [new LedPosition(0.5f, 0.5f)],
    };

    private static EffectRenderEngine CreateEngine(DeviceManager devices, ProfileService profiles)
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var telemetry = new TelemetryService(devices, Options.Create(new TelemetryOptions()), time, NullLogger<TelemetryService>.Instance);
        return new EffectRenderEngine(
            devices,
            profiles,
            telemetry,
            new EffectRegistry(new NullScreenCapturer(), new ProbeCatalog()),
            new SleepPrecisionTimer(),
            Options.Create(new RenderingOptions()),
            NullLogger<EffectRenderEngine>.Instance);
    }

    private static byte[] ZoneColor(EffectRenderEngine engine, string zoneId)
    {
        var preview = engine.LatestPreview;
        foreach (var zone in preview)
        {
            if (string.Equals(zone.ZoneId, zoneId, StringComparison.Ordinal))
            {
                return zone.Rgb;
            }
        }

        return [];
    }

    private static bool IsBlink(byte[] rgb) => rgb.AsSpan().SequenceEqual(Cyan) || rgb.AsSpan().SequenceEqual(Dim);

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(15, cancellationToken);
        }
    }

    private sealed class ProbeCatalog : IExternalEffectCatalog
    {
        public IReadOnlyList<EffectDescriptor> Descriptors { get; } = [ProbeDescriptor];

        public bool TryCreate(string effectId, out IEffect effect)
        {
            if (!string.Equals(effectId, ProbeDescriptor.Id, StringComparison.OrdinalIgnoreCase))
            {
                effect = null!;
                return false;
            }

            effect = new ProbeEffect();
            return true;
        }
    }

    private sealed class ProbeEffect : IEffect
    {
        public EffectDescriptor Descriptor => ProbeDescriptor;

        public void Render(Span<byte> rgba, in EffectFrameContext context)
        {
        }

        public bool TrySamplePoints(ReadOnlySpan<LedPoint> points, Span<RgbColor> destination, in EffectFrameContext context)
        {
            destination.Fill(RgbColor.Black);
            return true;
        }

        public void Dispose()
        {
        }
    }

    private sealed class SensorOnlyDevice : IHardwareDevice
    {
        public string Id => "sensor";

        public string Name => "sensor";

        public string Vendor => "Test";

        public DeviceCapabilities Capabilities => DeviceCapabilities.Sensors;

        public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class UnmanagedDevice : IHardwareDevice
    {
        public string Id => "idle";

        public string Name => "idle";

        public string Vendor => "Test";

        public DeviceCapabilities Capabilities => DeviceCapabilities.None;

        public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
