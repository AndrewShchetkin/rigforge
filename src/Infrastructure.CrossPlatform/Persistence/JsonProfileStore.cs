using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.Persistence;

/// <summary>Stores the profile as profile.json; writes go to a temp file first so a crash never leaves a torn file.</summary>
internal sealed class JsonProfileStore(IOptions<StorageOptions> options) : IProfileStore
{
    private const string FileName = "profile.json";

    public string FilePath { get; } = Path.Combine(ResolveDirectory(options.Value.DataDirectory), FileName);

    public async Task<RigProfile?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(FilePath);
        return await JsonSerializer.DeserializeAsync(stream, ProfileJsonContext.Default.RigProfile, cancellationToken);
    }

    public async Task SaveAsync(RigProfile profile, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";

        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, profile, ProfileJsonContext.Default.RigProfile, cancellationToken);
        }

        File.Move(temporary, FilePath, overwrite: true);
    }

    public static string ResolveDirectory(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured));
        }

        // Windows: %LOCALAPPDATA%\RigForge, Linux: $XDG_CONFIG_HOME/rigforge (~/.config/rigforge).
        return OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RigForge")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "rigforge");
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(RigProfile))]
internal sealed partial class ProfileJsonContext : JsonSerializerContext;
