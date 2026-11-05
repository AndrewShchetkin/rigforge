using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Application.Effects;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Persistence;
using Wasmtime;

namespace RigForge.Infrastructure.CrossPlatform.Effects;

/// <summary>
/// Loads <c>{data}/effects/*.wat</c> or <c>*.wasm</c> next to a sidecar <c>*.json</c>.
/// The native engine is created only when a module file is actually compiled. An empty directory does not load it.
/// </summary>
public sealed partial class WasmEffectCatalog : IExternalEffectCatalog, IDisposable
{
    public const int MaxWatBytes = 64 * 1024;
    public const int MaxWasmBytes = 256 * 1024;
    public const int MaxManifestBytes = 64 * 1024;

    private readonly ILogger<WasmEffectCatalog> _logger;
    private readonly Dictionary<string, LoadedModule> _modules = new(StringComparer.Ordinal);
    private readonly List<EffectDescriptor> _descriptors = [];
    private Engine? _engine;

    public WasmEffectCatalog(IOptions<StorageOptions> options, ILogger<WasmEffectCatalog> logger)
        : this(Path.Combine(JsonProfileStore.ResolveDirectory(options.Value.DataDirectory), "effects"), logger)
    {
    }

    public WasmEffectCatalog(string effectsDirectory, ILogger<WasmEffectCatalog> logger)
    {
        _logger = logger;
        Load(effectsDirectory);
    }

    public IReadOnlyList<EffectDescriptor> Descriptors => _descriptors;

    public bool TryCreate(string effectId, out IEffect effect)
    {
        if (_engine is null || !_modules.TryGetValue(effectId, out var loaded))
        {
            effect = null!;
            return false;
        }

        effect = new WasmEffect(loaded.Descriptor, _engine, loaded.Module);
        return true;
    }

    public void Dispose()
    {
        foreach (var loaded in _modules.Values)
        {
            loaded.Module.Dispose();
        }

        _modules.Clear();
        _engine?.Dispose();
        _engine = null;
    }

    private void Load(string effectsDirectory)
    {
        if (string.IsNullOrWhiteSpace(effectsDirectory))
        {
            return;
        }

        try
        {
            if (!Directory.Exists(effectsDirectory))
            {
                return;
            }

            LoadDirectory(effectsDirectory);
        }
        catch (Exception exception)
        {
            LogSkipped(effectsDirectory, exception.Message);
        }
    }

    private void LoadDirectory(string effectsDirectory)
    {
        var byStem = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(effectsDirectory).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var extension = Path.GetExtension(path);
            var isWat = extension.Equals(".wat", StringComparison.OrdinalIgnoreCase);
            var isWasm = extension.Equals(".wasm", StringComparison.OrdinalIgnoreCase);
            if (!isWat && !isWasm)
            {
                continue;
            }

            var stem = Path.GetFileNameWithoutExtension(path);
            if (!byStem.TryGetValue(stem, out var existing))
            {
                byStem[stem] = path;
                continue;
            }

            if (isWasm && existing.EndsWith(".wat", StringComparison.OrdinalIgnoreCase))
            {
                LogSkipped(existing, "A .wasm module with the same name is present; the text module was ignored.");
                byStem[stem] = path;
            }
            else
            {
                LogSkipped(path, "Another module with the same file name was already selected.");
            }
        }

        foreach (var path in byStem.Values.OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            TryLoad(path);
        }
    }

    private void TryLoad(string path)
    {
        try
        {
            TryLoadCore(path);
        }
        catch (Exception exception)
        {
            LogSkipped(path, exception.Message);
        }
    }

    private void TryLoadCore(string path)
    {
        var extension = Path.GetExtension(path);
        var isWat = extension.Equals(".wat", StringComparison.OrdinalIgnoreCase);
        var maxBytes = isWat ? MaxWatBytes : MaxWasmBytes;
        var length = new FileInfo(path).Length;
        if (length > maxBytes)
        {
            LogSkipped(path, $"Module is larger than {maxBytes} bytes.");
            return;
        }

        var manifestPath = Path.ChangeExtension(path, ".json");
        if (!File.Exists(manifestPath))
        {
            LogSkipped(path, "The sidecar .json manifest is missing.");
            return;
        }

        if (new FileInfo(manifestPath).Length > MaxManifestBytes)
        {
            LogSkipped(manifestPath, $"Manifest is larger than {MaxManifestBytes} bytes.");
            return;
        }

        if (!TryReadManifest(manifestPath, out var descriptor, out var reason))
        {
            LogSkipped(manifestPath, reason);
            return;
        }

        if (_modules.ContainsKey(descriptor.Id))
        {
            LogSkipped(path, $"Effect id '{descriptor.Id}' is already loaded.");
            return;
        }

        Module? module = null;
        try
        {
            _engine ??= WasmEffectHost.CreateEngine();
            module = isWat ? Module.FromTextFile(_engine, path) : Module.FromFile(_engine, path);
            if (!TryAcceptModule(module, out reason))
            {
                LogSkipped(path, reason);
                module.Dispose();
                return;
            }

            using (var store = WasmEffectHost.CreateStore(_engine))
            {
                store.Fuel = WasmGuestLayout.InstantiateFuel;
                _ = new Instance(store, module);
            }

            _modules.Add(descriptor.Id, new LoadedModule(descriptor, module));
            _descriptors.Add(descriptor);
            module = null;
        }
        catch (Exception exception)
        {
            module?.Dispose();
            LogSkipped(path, exception.Message);
        }
    }

    private static bool TryAcceptModule(Module module, out string reason)
    {
        if (module.Imports.Count > 0)
        {
            reason = "Imports are not allowed.";
            return false;
        }

        var memories = module.Exports.OfType<MemoryExport>().ToArray();
        if (memories.Length != 1 || !string.Equals(memories[0].Name, WasmGuestLayout.MemoryExport, StringComparison.Ordinal))
        {
            reason = "The module must export exactly one memory named 'memory'.";
            return false;
        }

        var memory = memories[0];
        if (memory.Is64Bit || memory.Maximum is not long pages || pages < 1 || pages > WasmGuestLayout.MaxMemoryPages)
        {
            reason = $"Memory must be 32-bit with a maximum of 1..{WasmGuestLayout.MaxMemoryPages} pages.";
            return false;
        }

        var functions = module.Exports.OfType<FunctionExport>()
            .Where(export => string.Equals(export.Name, WasmGuestLayout.SampleExport, StringComparison.Ordinal))
            .ToArray();
        if (functions.Length != 1
            || functions[0].Parameters.Count != 0
            || functions[0].Results.Count != 1
            || functions[0].Results[0] != ValueKind.Int32)
        {
            reason = "The module must export sample() -> i32.";
            return false;
        }

        reason = "";
        return true;
    }

    private static bool TryReadManifest(string path, out EffectDescriptor descriptor, out string reason)
    {
        descriptor = null!;
        try
        {
            var manifest = JsonSerializer.Deserialize(File.ReadAllBytes(path), WasmEffectJsonContext.Default.WasmEffectManifest);
            if (manifest is null)
            {
                reason = "Manifest is empty.";
                return false;
            }

            return TryCreateDescriptor(manifest, out descriptor, out reason);
        }
        catch (JsonException exception)
        {
            reason = exception.Message;
            return false;
        }
    }

    private static bool TryCreateDescriptor(WasmEffectManifest manifest, out EffectDescriptor descriptor, out string reason)
    {
        descriptor = null!;
        var id = manifest.Id ?? "";
        if (!EffectIdPattern().IsMatch(id))
        {
            reason = "Effect id must match [a-z0-9-]{1,64}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 80)
        {
            reason = "Effect name must be 1..80 characters.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(manifest.Description) || manifest.Description.Length > 400)
        {
            reason = "Effect description must be 1..400 characters.";
            return false;
        }

        var definitions = new List<EffectParameterDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in manifest.Parameters ?? [])
        {
            if (!TryCreateParameter(parameter, out var definition, out reason))
            {
                return false;
            }

            if (!seen.Add(definition.Key))
            {
                reason = $"Parameter '{definition.Key}' is declared more than once.";
                return false;
            }

            definitions.Add(definition);
        }

        descriptor = new EffectDescriptor(id, manifest.Name.Trim(), manifest.Description.Trim(), manifest.IsAnimated, definitions);
        reason = "";
        return true;
    }

    private static bool TryCreateParameter(WasmEffectParameterManifest parameter, out EffectParameterDefinition definition, out string reason)
    {
        definition = null!;
        var key = parameter.Key ?? "";
        var label = parameter.Label?.Trim() ?? "";
        if (label.Length is 0 or > 80)
        {
            reason = $"Parameter '{key}' needs a label of 1..80 characters.";
            return false;
        }

        if (string.Equals(key, EffectParameterValues.ColorsKey, StringComparison.Ordinal))
        {
            if (!string.Equals(parameter.Kind, nameof(EffectParameterKind.ColorList), StringComparison.Ordinal))
            {
                reason = "Parameter 'colors' must be a ColorList.";
                return false;
            }

            if (parameter.MinColors < 1 || parameter.MaxColors > WasmGuestLayout.MaxColors || parameter.MinColors > parameter.MaxColors)
            {
                reason = $"Parameter 'colors' must allow 1..{WasmGuestLayout.MaxColors} colors.";
                return false;
            }

            if (!TryReadColors(parameter.DefaultValue, parameter.MinColors, parameter.MaxColors, out var colors, out reason))
            {
                return false;
            }

            definition = EffectParameterDefinition.ColorList(key, label, colors, parameter.MinColors, parameter.MaxColors);
            reason = "";
            return true;
        }

        if (key is not (EffectParameterValues.SpeedKey or EffectParameterValues.AngleKey))
        {
            reason = $"Parameter '{key}' is not supported. Use colors, speed, or angle.";
            return false;
        }

        if (!string.Equals(parameter.Kind, nameof(EffectParameterKind.FloatRange), StringComparison.Ordinal))
        {
            reason = $"Parameter '{key}' must be a FloatRange.";
            return false;
        }

        if (parameter.Min is not double min || parameter.Max is not double max || parameter.Step is not double step || !(min < max) || !(step > 0))
        {
            reason = $"Parameter '{key}' needs min < max and a positive step.";
            return false;
        }

        if (parameter.DefaultValue.ValueKind != JsonValueKind.Number || !parameter.DefaultValue.TryGetDouble(out var defaultValue) || defaultValue < min || defaultValue > max)
        {
            reason = $"Parameter '{key}' default must be a number inside its range.";
            return false;
        }

        if (parameter.Unit is { Length: > 16 })
        {
            reason = $"Parameter '{key}' unit must be at most 16 characters.";
            return false;
        }

        definition = EffectParameterDefinition.FloatRange(key, label, defaultValue, min, max, step, parameter.Unit);
        reason = "";
        return true;
    }

    private static bool TryReadColors(JsonElement value, int minColors, int maxColors, out List<string> colors, out string reason)
    {
        colors = [];
        if (value.ValueKind != JsonValueKind.Array)
        {
            reason = "Parameter 'colors' default must be an array of #RRGGBB strings.";
            return false;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !RgbColor.TryParse(item.GetString(), out _))
            {
                reason = "Parameter 'colors' default must be an array of #RRGGBB strings.";
                return false;
            }

            colors.Add(item.GetString()!);
        }

        if (colors.Count < minColors || colors.Count > maxColors)
        {
            reason = "Parameter 'colors' default does not match minColors and maxColors.";
            return false;
        }

        reason = "";
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped external effect {Path}: {Reason}")]
    private partial void LogSkipped(string path, string reason);

    [GeneratedRegex("^[a-z0-9-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex EffectIdPattern();

    private sealed class LoadedModule(EffectDescriptor descriptor, Module module)
    {
        public EffectDescriptor Descriptor { get; } = descriptor;
        public Module Module { get; } = module;
    }
}

internal sealed class WasmEffectManifest
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool IsAnimated { get; set; }
    public WasmEffectParameterManifest[]? Parameters { get; set; }
}

internal sealed class WasmEffectParameterManifest
{
    public string? Key { get; set; }
    public string? Label { get; set; }
    public string? Kind { get; set; }
    public JsonElement DefaultValue { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Step { get; set; }
    public string? Unit { get; set; }
    public int MinColors { get; set; }
    public int MaxColors { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WasmEffectManifest))]
internal sealed partial class WasmEffectJsonContext : JsonSerializerContext;
