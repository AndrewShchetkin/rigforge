using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RigForge.Application.Effects;
using RigForge.Application.Effects.Library;
using RigForge.Application.Profiles;
using RigForge.Application.Validation;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform;
using RigForge.Infrastructure.CrossPlatform.Persistence;
using RigForge.Infrastructure.CrossPlatform.Platform;

namespace RigForge.UnitTests.Profiles;

public sealed class EffectParameterTests
{
    private readonly EffectRegistry _registry = new(new NullScreenCapturer());
    private readonly RigProfileValidator _validator;

    public EffectParameterTests()
    {
        _validator = new RigProfileValidator(_registry);
    }

    private bool IsValid(EffectSettings effect) => _validator.Validate(new RigProfile { Effect = effect }).IsValid;

    [Fact]
    public void Missing_parameters_fall_back_to_schema_defaults_and_are_valid()
    {
        Assert.True(IsValid(new EffectSettings { EffectId = TemperatureEffect.EffectId }));
        Assert.Equal(2, RigProfile.CurrentSchemaVersion);
    }

    [Fact]
    public void Unknown_parameter_keys_are_rejected()
    {
        var effect = EffectSettingsFactory.Create(SolidColorEffect.EffectId, ("bogus", 1));

        Assert.False(IsValid(effect));
    }

    [Fact]
    public void Wrong_parameter_types_are_rejected()
    {
        var effect = EffectSettingsFactory.Create(
            BreathingEffect.EffectId,
            ("colors", "#FF0000"),
            ("speed", "fast"));

        Assert.False(IsValid(effect));
    }

    [Fact]
    public void Out_of_range_float_parameters_are_rejected()
    {
        var effect = EffectSettingsFactory.Create(BreathingEffect.EffectId, ("speed", 100f));

        Assert.False(IsValid(effect));
    }

    [Fact]
    public void Color_lists_outside_the_schema_bounds_are_rejected()
    {
        var tooFew = EffectSettingsFactory.Create(GradientWaveEffect.EffectId, ("colors", new[] { "#FF0000" }));
        var tooMany = EffectSettingsFactory.Create(
            SolidColorEffect.EffectId,
            ("colors", new[] { "#FF0000", "#00FF00" }));
        var malformed = EffectSettingsFactory.Create(SolidColorEffect.EffectId, ("colors", new[] { "red" }));

        Assert.False(IsValid(tooFew));
        Assert.False(IsValid(tooMany));
        Assert.False(IsValid(malformed));
    }

    [Fact]
    public void Empty_sensor_selector_is_rejected()
    {
        var effect = EffectSettingsFactory.Create(TemperatureEffect.EffectId, ("sensorSelector", ""));

        Assert.False(IsValid(effect));
    }

    [Fact]
    public void Temperature_high_must_be_above_low()
    {
        var effect = EffectSettingsFactory.Create(
            TemperatureEffect.EffectId,
            ("temperatureLow", 80f),
            ("temperatureHigh", 35f));

        Assert.False(IsValid(effect));
    }

    [Fact]
    public void Fill_defaults_completes_every_schema_key()
    {
        var filled = _registry.FillDefaults(new EffectSettings { EffectId = TemperatureEffect.EffectId });

        Assert.Equal(
            ["colors", "sensorSelector", "temperatureHigh", "temperatureLow"],
            filled.Parameters.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("role:CpuTemperature", filled.Parameters["sensorSelector"].GetString());
    }

    [Fact]
    public void Legacy_schema_version_1_settings_fold_flat_fields_into_parameters()
    {
        const string json = """
            {
              "effectId": "gradient",
              "brightness": 0.5,
              "speed": 2,
              "colors": ["#FF0000", "#00FF00"],
              "angle": 90,
              "targetFps": 30
            }
            """;

        var effect = JsonSerializer.Deserialize<EffectSettings>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(effect);
        Assert.Equal("gradient", effect.EffectId);
        Assert.Equal(0.5f, effect.Brightness);
        Assert.Equal(30, effect.TargetFps);
        Assert.Equal(2f, effect.Parameters["speed"].GetSingle());
        Assert.Equal(90f, effect.Parameters["angle"].GetSingle());
        Assert.Equal(["#FF0000", "#00FF00"], effect.Parameters["colors"].EnumerateArray().Select(c => c.GetString()).ToArray());
        Assert.False(effect.Parameters.ContainsKey("brightness"));
    }

    [Fact]
    public void Current_format_round_trips_without_legacy_fields()
    {
        var effect = EffectSettingsFactory.Create(
            GradientWaveEffect.EffectId,
            ("colors", new[] { "#FF0000", "#00FF00" }),
            ("speed", 2f));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var json = JsonSerializer.Serialize(effect, options);
        var roundTripped = JsonSerializer.Deserialize<EffectSettings>(json, options);

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("speed", out _));
        Assert.False(document.RootElement.TryGetProperty("colors", out _));
        Assert.True(document.RootElement.TryGetProperty("parameters", out _));
        Assert.NotNull(roundTripped);
        Assert.Equal("gradient", roundTripped.EffectId);
        Assert.Equal(2f, roundTripped.Parameters["speed"].GetSingle());
    }

    [Fact]
    public async Task Legacy_profile_is_rewritten_as_schema_version_2_with_defaults_filled()
    {
        var legacy = new RigProfile
        {
            SchemaVersion = 1,
            Effect = EffectSettingsFactory.Create(
                TemperatureEffect.EffectId,
                ("colors", new[] { "#0000FF", "#FF0000" }),
                ("temperatureLow", 20f),
                ("temperatureHigh", 80f)),
        };
        var store = new InMemoryProfileStore { Saved = legacy };
        var service = new ProfileService(store, _validator, NullLogger<ProfileService>.Instance, _registry);

        await service.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(RigProfile.CurrentSchemaVersion, service.Current.SchemaVersion);
        Assert.Equal(RigProfile.CurrentSchemaVersion, store.Saved!.SchemaVersion);
        Assert.True(service.Current.Effect.Parameters.ContainsKey("sensorSelector"));
        Assert.Equal(20f, service.Current.Effect.Parameters["temperatureLow"].GetSingle());
    }

    [Fact]
    public async Task Profile_json_file_is_migrated_from_schema_version_1_to_2()
    {
        var directory = Directory.CreateTempSubdirectory("rigforge-profile-test");
        try
        {
            var path = Path.Combine(directory.FullName, "profile.json");
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "schemaVersion": 1,
                  "effect": {
                    "effectId": "temperature",
                    "brightness": 0.8,
                    "colors": ["#0000FF", "#FF0000"],
                    "sensorSelector": "role:CpuTemperature",
                    "temperatureLow": 20,
                    "temperatureHigh": 80,
                    "targetFps": 30
                  }
                }
                """,
                TestContext.Current.CancellationToken);
            var store = new JsonProfileStore(Options.Create(new StorageOptions { DataDirectory = directory.FullName }));
            var service = new ProfileService(store, _validator, NullLogger<ProfileService>.Instance, _registry);

            await service.InitializeAsync(TestContext.Current.CancellationToken);

            Assert.Equal(RigProfile.CurrentSchemaVersion, service.Current.SchemaVersion);
            Assert.Equal(20f, service.Current.Effect.Parameters["temperatureLow"].GetSingle());

            using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Equal(2, saved.RootElement.GetProperty("schemaVersion").GetInt32());
            var effect = saved.RootElement.GetProperty("effect");
            Assert.False(effect.TryGetProperty("colors", out _));
            Assert.Equal(20, effect.GetProperty("parameters").GetProperty("temperatureLow").GetInt32());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Updating_an_effect_fills_missing_parameters_before_saving()
    {
        var store = new InMemoryProfileStore();
        var service = new ProfileService(store, _validator, NullLogger<ProfileService>.Instance, _registry);

        var result = await service.UpdateEffectAsync(
            new EffectSettings { EffectId = SolidColorEffect.EffectId },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Equal(["#FF0080"], service.Current.Effect.Parameters["colors"].EnumerateArray().Select(c => c.GetString()).ToArray());
        Assert.Equal(["#FF0080"], store.Saved!.Effect.Parameters["colors"].EnumerateArray().Select(c => c.GetString()).ToArray());
    }
}
