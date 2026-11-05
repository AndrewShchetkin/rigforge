using Microsoft.Extensions.Logging.Abstractions;
using RigForge.Application.Effects;
using RigForge.Application.Effects.Library;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Effects;
using RigForge.Infrastructure.CrossPlatform.Platform;
using Wasmtime;

namespace RigForge.UnitTests.Effects;

public sealed class WasmEffectCatalogTests
{
    private const string ValidModule = """
        (module
          (memory (export "memory") 1 32)
          (func (export "sample") (result i32)
            i32.const 0))
        """;

    private const string ImportingModule = """
        (module
          (import "host" "trap" (func))
          (memory (export "memory") 1 32)
          (func (export "sample") (result i32)
            i32.const 0))
        """;

    private const string UnboundedMemoryModule = """
        (module
          (memory (export "memory") 1)
          (func (export "sample") (result i32)
            i32.const 0))
        """;

    private const string HugeMemoryModule = """
        (module
          (memory (export "memory") 1 33)
          (func (export "sample") (result i32)
            i32.const 0))
        """;

    private const string SecondMemoryModule = """
        (module
          (memory (export "memory") 1 32)
          (memory (export "other") 1 1)
          (func (export "sample") (result i32)
            i32.const 0))
        """;

    private const string InfiniteLoopModule = """
        (module
          (memory (export "memory") 1 32)
          (func (export "sample") (result i32)
            (loop $forever
              (br $forever))
            unreachable))
        """;

    [Fact]
    public void Plasma_blends_the_palette_and_moves()
    {
        using var catalog = new WasmEffectCatalog(RepoEffectsDirectory(), NullLogger<WasmEffectCatalog>.Instance);
        Assert.True(catalog.TryCreate("wasm-plasma", out var effect));
        using (effect)
        {
            Assert.True(effect.Descriptor.IsAnimated);
            LedPoint[] points = [new(10.5f, 4f, true), new(300.2f, 80f, true), new(1f, 1f, false)];
            var destination = new RgbColor[points.Length];
            var solid = Context(0.4, speed: 1f, angle: 15f, new RgbColor(0, 255, 0));
            Assert.True(effect.TrySamplePoints(points, destination, in solid));
            Assert.Equal(new RgbColor(0, 255, 0), destination[0]);
            Assert.Equal(new RgbColor(0, 255, 0), destination[1]);
            Assert.Equal(RgbColor.Black, destination[2]);

            RgbColor[] palette = [new(255, 0, 0), new(0, 0, 255)];
            var early = Context(0.2, speed: 1f, angle: 20f, palette);
            Assert.True(effect.TrySamplePoints(points, destination, in early));
            Assert.Equal(0, destination[0].G);
            Assert.Equal(0, destination[1].G);
            Assert.NotEqual(destination[0], destination[1]);
            var first = destination[0];

            var later = Context(1.4, speed: 1f, angle: 20f, palette);
            Assert.True(effect.TrySamplePoints(points, destination, in later));
            Assert.NotEqual(first, destination[0]);

            var turned = Context(0.2, speed: 1f, angle: 140f, palette);
            Assert.True(effect.TrySamplePoints(points, destination, in turned));
            Assert.NotEqual(first, destination[0]);
        }
    }

    [Fact]
    public void Plasma_render_paints_a_canvas_and_later_samples_still_work()
    {
        using var catalog = new WasmEffectCatalog(RepoEffectsDirectory(), NullLogger<WasmEffectCatalog>.Instance);
        Assert.True(catalog.TryCreate("wasm-plasma", out var effect));
        using (effect)
        {
            var rgba = new byte[320 * 180 * 4];
            var green = Context(0.4, speed: 1f, angle: 15f, new RgbColor(0, 255, 0));
            effect.Render(rgba, in green);

            Assert.Equal(new byte[] { 0, 255, 0, 255 }, rgba[..4]);
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, rgba[^4..]);

            RgbColor[] palette = [new(255, 0, 0), new(0, 0, 255)];
            var mixed = Context(0.2, speed: 1f, angle: 20f, palette);
            effect.Render(rgba, in mixed);

            var left = Pixel(rgba, 10, 4);
            var right = Pixel(rgba, 300, 80);
            Assert.NotEqual(left, right);
            Assert.Equal(0, left.G);
            Assert.Equal(0, right.G);
            Assert.Equal(255, rgba[3]);

            LedPoint[] points = [new(10.5f, 4f, true)];
            var destination = new RgbColor[1];
            Assert.True(effect.TrySamplePoints(points, destination, in mixed));
            Assert.NotEqual(RgbColor.Black, destination[0]);
            Assert.Equal(0, destination[0].G);
        }
    }

    [Fact]
    public void Plasma_samples_allocate_nothing_after_warmup()
    {
        using var catalog = new WasmEffectCatalog(RepoEffectsDirectory(), NullLogger<WasmEffectCatalog>.Instance);
        Assert.True(catalog.TryCreate("wasm-plasma", out var effect));
        using (effect)
        {
            LedPoint[] points = [new(10.5f, 20f, true), new(40f, 8f, true), new(1f, 1f, false)];
            var destination = new RgbColor[points.Length];
            var context = Context(time: 1.5, speed: 1f);

            for (var iteration = 0; iteration < 100; iteration++)
            {
                effect.TrySamplePoints(points, destination, in context);
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var iteration = 0; iteration < 100; iteration++)
            {
                effect.TrySamplePoints(points, destination, in context);
            }

            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }

    [Fact]
    public void Imports_and_illegal_memory_are_left_out_of_the_catalog()
    {
        var directory = Directory.CreateTempSubdirectory("rigforge-wasm");
        try
        {
            WriteModule(directory.FullName, "importing", ImportingModule, Manifest("importing"));
            WriteModule(directory.FullName, "unbounded", UnboundedMemoryModule, Manifest("unbounded"));
            WriteModule(directory.FullName, "huge", HugeMemoryModule, Manifest("huge"));
            WriteModule(directory.FullName, "second", SecondMemoryModule, Manifest("second"));
            WriteModule(directory.FullName, "sensor", ValidModule, """
                {
                  "id": "sensor-glow",
                  "name": "Sensor",
                  "description": "Uses a sensor, which this slice does not pass in.",
                  "isAnimated": false,
                  "parameters": [
                    { "key": "sensorSelector", "label": "Sensor", "kind": "SensorSelector", "defaultValue": "role:CpuTemperature" }
                  ]
                }
                """);

            using var catalog = new WasmEffectCatalog(directory.FullName, NullLogger<WasmEffectCatalog>.Instance);
            Assert.Empty(catalog.Descriptors);
            Assert.False(catalog.TryCreate("importing", out _));
            Assert.False(catalog.TryCreate("huge", out _));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Infinite_loop_traps_on_fuel_and_later_frames_stay_black()
    {
        var directory = Directory.CreateTempSubdirectory("rigforge-wasm");
        try
        {
            WriteModule(directory.FullName, "loop", InfiniteLoopModule, Manifest("loop"));
            using var catalog = new WasmEffectCatalog(directory.FullName, NullLogger<WasmEffectCatalog>.Instance);
            Assert.True(catalog.TryCreate("loop", out var effect));
            using (effect)
            {
                LedPoint[] points = [new(1f, 2f, true)];
                var destination = new RgbColor[1];
                var started = System.Diagnostics.Stopwatch.StartNew();

                var context = Context(0, 1f);
                var exception = Assert.Throws<TrapException>(() => effect.TrySamplePoints(points, destination, in context));
                Assert.Contains("fuel", exception.Message, StringComparison.OrdinalIgnoreCase);
                Assert.True(effect.TrySamplePoints(points, destination, in context));

                Assert.Equal(RgbColor.Black, destination[0]);
                Assert.True(started.Elapsed < TimeSpan.FromSeconds(5), $"Fuel trap took {started.Elapsed}.");
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Built_in_effect_id_is_kept_when_an_external_module_reuses_it()
    {
        var directory = Directory.CreateTempSubdirectory("rigforge-wasm");
        try
        {
            WriteModule(directory.FullName, "collision", ValidModule, Manifest("rainbow-wave", "Not the built-in"));
            using var catalog = new WasmEffectCatalog(directory.FullName, NullLogger<WasmEffectCatalog>.Instance);
            var registry = new EffectRegistry(
                new NullScreenCapturer(),
                catalog,
                NullLogger<EffectRegistry>.Instance);

            var descriptor = Assert.Single(registry.Descriptors, effect => effect.Id == RainbowWaveEffect.EffectId);
            Assert.Equal(RainbowWaveEffect.Info.Name, descriptor.Name);
            using var effect = registry.Create(RainbowWaveEffect.EffectId);
            Assert.IsType<RainbowWaveEffect>(effect);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static RgbColor Pixel(byte[] rgba, int x, int y)
    {
        var i = (y * 320 + x) * 4;
        return new RgbColor(rgba[i], rgba[i + 1], rgba[i + 2]);
    }

    private static EffectFrameContext Context(double time, float speed) =>
        Context(time, speed, angle: 15f, new RgbColor(255, 0, 128));

    private static EffectFrameContext Context(double time, float speed, float angle, params RgbColor[] colors) =>
        new(
            320,
            180,
            time,
            EffectSettingsFactory.Create("wasm-plasma"),
            colors,
            TelemetrySnapshot.Empty,
            Speed: speed,
            Angle: angle);

    private static string RepoEffectsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "effects", "wasm-plasma.wat");
            if (File.Exists(candidate))
            {
                return Path.Combine(dir.FullName, "effects");
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("effects/wasm-plasma.wat was not found from the test output directory.");
    }

    private static void WriteModule(string directory, string name, string wat, string manifest)
    {
        File.WriteAllText(Path.Combine(directory, name + ".wat"), wat);
        File.WriteAllText(Path.Combine(directory, name + ".json"), manifest);
    }

    private static string Manifest(string id, string? name = null) => $$"""
        {
          "id": "{{id}}",
          "name": "{{name ?? "External"}}",
          "description": "Loaded from a unit test.",
          "isAnimated": true,
          "parameters": []
        }
        """;
}
