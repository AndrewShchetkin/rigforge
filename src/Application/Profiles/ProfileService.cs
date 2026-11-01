using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Logging;
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
public sealed partial class ProfileService(IProfileStore store, IValidator<RigProfile> validator, ILogger<ProfileService> logger)
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

        _current = loaded;
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

        return fallback with
        {
            Canvas = canvas,
            Effect = effect,
            Lcd = lcd,
            FanCurves = keptCurves,
        };
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
