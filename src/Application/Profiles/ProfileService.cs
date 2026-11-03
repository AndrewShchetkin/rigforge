using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Logging;
using RigForge.Application.Effects;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Application.Profiles;

public sealed record ProfileUpdateResult(RigProfile? Profile, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Holds the active <see cref="RigProfile"/>. Engines read <see cref="Current"/> (an immutable record) every
/// iteration, so updates become visible without locks; writes are validated, persisted and then published.
/// </summary>
public sealed partial class ProfileService(
    IProfileStore store,
    IValidator<RigProfile> validator,
    ILogger<ProfileService> logger,
    EffectRegistry? effects = null)
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private volatile RigProfile _current = new();

    public event Action<RigProfile>? Changed;

    public RigProfile Current => _current;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        RigProfile? loaded;
        try
        {
            loaded = await store.LoadAsync(cancellationToken);
        }
        catch (JsonException ex)
        {
            try
            {
                await store.BackupCorruptAsync(cancellationToken);
            }
            catch (Exception backupEx) when (backupEx is not OperationCanceledException)
            {
                LogRecoveryPersistFailed(backupEx);
            }

            LogLoadFailed(ex);
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLoadFailed(ex);
            return;
        }

        if (loaded is null)
        {
            LogUsingDefaults();
            return;
        }

        // Source-generated JSON leaves a missing colorCorrections array null. Treat that as empty
        // so an older profile.json stays valid. An explicit null that skips this step still fails.
        loaded = loaded.ColorCorrections is null ? loaded with { ColorCorrections = [] } : loaded;

        var validation = await validator.ValidateAsync(loaded, cancellationToken);
        if (!validation.IsValid)
        {
            LogInvalidProfile(string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));
            try
            {
                await store.BackupCorruptAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRecoveryPersistFailed(ex);
            }

            var salvaged = await SalvageAsync(loaded, cancellationToken);
            _current = salvaged;
            try
            {
                await store.SaveAsync(salvaged, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRecoveryPersistFailed(ex);
            }

            return;
        }

        // One-time migration: schemaVersion 1 files (already folded into Parameters by the JSON
        // converter) are rewritten in the current format with schema defaults filled in.
        var normalized = NormalizeProfile(loaded);
        _current = normalized;
        if (normalized.SchemaVersion != loaded.SchemaVersion || !ReferenceEquals(normalized.Effect, loaded.Effect))
        {
            try
            {
                await store.SaveAsync(normalized, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRecoveryPersistFailed(ex);
            }
        }
    }

    /// <summary>Brings a profile to the current schema: version bumped, effect parameters completed from defaults.</summary>
    private RigProfile NormalizeProfile(RigProfile profile)
    {
        var effect = effects is null || profile.Effect is null ? profile.Effect : effects.FillDefaults(profile.Effect);
        return profile.SchemaVersion == RigProfile.CurrentSchemaVersion && ReferenceEquals(effect, profile.Effect)
            ? profile
            : profile with { SchemaVersion = RigProfile.CurrentSchemaVersion, Effect = effect! };
    }

    private async Task<RigProfile> SalvageAsync(RigProfile loaded, CancellationToken cancellationToken)
    {
        var fallback = new RigProfile();

        var canvas = loaded.Canvas is not null && (await validator.ValidateAsync(fallback with { Canvas = loaded.Canvas }, cancellationToken)).IsValid
            ? loaded.Canvas
            : fallback.Canvas;

        var effect = loaded.Effect is not null && (await validator.ValidateAsync(fallback with { Effect = loaded.Effect }, cancellationToken)).IsValid
            ? loaded.Effect
            : fallback.Effect;

        var lcd = loaded.Lcd is not null && (await validator.ValidateAsync(fallback with { Lcd = loaded.Lcd }, cancellationToken)).IsValid
            ? loaded.Lcd
            : fallback.Lcd;

        var keptCurves = new List<FanCurve>();
        if (loaded.FanCurves is not null)
        {
            foreach (var curve in loaded.FanCurves)
            {
                if (curve is not null && (await validator.ValidateAsync(fallback with { FanCurves = [..keptCurves, curve] }, cancellationToken)).IsValid)
                {
                    keptCurves.Add(curve);
                }
            }
        }

        var keptCorrections = new List<DeviceColorCorrection>();
        if (loaded.ColorCorrections is not null)
        {
            foreach (var correction in loaded.ColorCorrections)
            {
                if (correction is not null
                    && (await validator.ValidateAsync(fallback with { ColorCorrections = [..keptCorrections, correction] }, cancellationToken)).IsValid)
                {
                    keptCorrections.Add(correction);
                }
            }
        }

        return NormalizeProfile(fallback with
        {
            Canvas = canvas,
            Effect = effect,
            Lcd = lcd,
            FanCurves = keptCurves,
            ColorCorrections = keptCorrections,
        });
    }

    /// <summary>Applies <paramref name="change"/> atomically. Invalid results are rejected and reported, not thrown.</summary>
    public async Task<ProfileUpdateResult> UpdateAsync(Func<RigProfile, RigProfile> change, CancellationToken cancellationToken)
    {
        RigProfile next;
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            next = change(_current);
            var validation = await validator.ValidateAsync(next, cancellationToken);
            if (!validation.IsValid)
            {
                return new ProfileUpdateResult(null, validation.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}").ToArray());
            }

            next = NormalizeProfile(next);
            await store.SaveAsync(next, cancellationToken);
            _current = next;
        }
        finally
        {
            _writeLock.Release();
        }

        Changed?.Invoke(next);
        return new ProfileUpdateResult(next, []);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not read the saved profile; starting with defaults")]
    private partial void LogLoadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "No saved profile found; starting with defaults")]
    private partial void LogUsingDefaults();

    [LoggerMessage(Level = LogLevel.Error, Message = "Saved profile is invalid and was ignored: {Errors}")]
    private partial void LogInvalidProfile(string errors);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not persist profile recovery backup or salvaged profile")]
    private partial void LogRecoveryPersistFailed(Exception exception);
}
