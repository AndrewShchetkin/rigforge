---
name: add-lighting-effect
description: >-
  Adds a built-in RigForge lighting effect from a plain-language description.
  Use when the user asks to add, create, or design an RGB effect, animation,
  palette motion, or lighting mode, including "эффект по описанию".
---

# Add a lighting effect

Turn a description into one built-in procedural effect. The gallery reads descriptors from the daemon, so do not edit React to register it.

## Decide from the description

- Default: a `ProceduralEffect` whose `Shade` colors one canvas point. Use this unless the description needs screen pixels or neighbor cells.
- `EffectCategory.Static` and `IsAnimated: false` only for a still color.
- `Flowing` for waves, curtains, and gradients that travel.
- `Spatial` for spirals, rings, rain, or a beam whose shape is the point.
- `Reactive` for a sensor, the desktop, or audio. Set `UsesAudio: true` only when `Shade` reads `context.AudioLevel` or `context.Band`.
- A sensor effect may set `IsAnimated: false`. The loop wakes it on telemetry. Name the parameter key `sensorSelector`.
- Return `false` from `TrySamplePoints` only for capture or cellular effects (see `AmbilightEffect`). Do not subclass `ProceduralEffect` in that case.

A user-supplied module is not this skill. Those are `{DataDirectory}/effects/<id>.wasm` plus a sibling `.json` like `effects/wasm-plasma.json`, and an id that does not match a built-in.

## Implement

1. Add `src/Application/Effects/Library/<Name>Effect.cs`.
   - `EffectId` is unique kebab-case.
   - `Info` uses `EffectParameterDefinition` factories. Put the same defaults in the schema and in `GetFloat` / `GetInt` / `GetBool` / `GetString` fallbacks.
   - `colors` and `speed` are reserved keys. Read them as `context.Colors` and `context.Speed`. Read `angle` as `context.Angle` when the parameter key is `angle`.
   - `Shade(float u, float v, in EffectFrameContext context)` uses `u` and `v` in 0..1. Color with `PaletteHelper.SampleGradient`, `PaletteHelper.Frac`, `PaletteHelper.ValueNoise`, and `RgbColor.Scale`. `Shade` allocates nothing: no LINQ, no new objects, no interpolated strings.
2. Register `(Info, () => new <Name>Effect())` in the `EffectRegistry` constructor.
3. Update `tests/RigForge.UnitTests/Effects/BuiltInEffectsLibraryTests.cs`:
   - Add the catalog row `(id, name, category, animated, audio)`.
   - Set the `Assert.Equal` count to `Catalog.Length` only. It must match the row count.
   - Add an `AssertSchema` line whose value is `key:Kind` in parameter order, joined by commas.
   - Add `[InlineData("<id>")]` to the procedural sample test and, for a `ProceduralEffect`, to the render-matches-sampling test.

## Check

Run `dotnet test --project tests/RigForge.UnitTests --filter BuiltInEffectsLibraryTests`.

Do not change device protocols, the installer, or the profile schema version. A new parameter key is stored in `EffectSettings.Parameters` and needs no migration.
