using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Application.Devices;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Application.Telemetry;

/// <summary>
/// Polls every sensor provider in parallel and publishes an immutable <see cref="TelemetrySnapshot"/>.
/// Readings survive short provider hiccups (until <see cref="TelemetryOptions.ReadingTimeToLive"/>);
/// consumers decide themselves when a reading is too old via <see cref="SensorReading.Timestamp"/>.
/// </summary>
public sealed partial class TelemetryService(
    DeviceManager devices,
    IOptions<TelemetryOptions> options,
    TimeProvider timeProvider,
    ILogger<TelemetryService> logger)
{
    private readonly Dictionary<string, SensorReading> _readings = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _failingProviders = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _providerGates = new(StringComparer.Ordinal);
    private volatile TelemetrySnapshot _latest = TelemetrySnapshot.Empty;

    public event Action<TelemetrySnapshot>? Updated;

    public TelemetrySnapshot Latest => _latest;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval, timeProvider);
        do
        {
            await PollOnceAsync(cancellationToken);
        }
        while (await timer.WaitForNextTickAsync(cancellationToken));
    }

    public async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        var providers = devices.SensorProviders;
        var results = await Task.WhenAll(providers.Select(p => ReadProviderAsync(p, cancellationToken)));

        var now = timeProvider.GetUtcNow();
        foreach (var reading in results.SelectMany(r => r))
        {
            _readings[reading.Id] = reading with { Timestamp = now };
        }

        var expiry = now - options.Value.ReadingTimeToLive;
        foreach (var stale in _readings.Where(r => r.Value.Timestamp < expiry).Select(r => r.Key).ToList())
        {
            _readings.Remove(stale);
        }

        var snapshot = new TelemetrySnapshot(now, _readings.Values.ToArray());
        _latest = snapshot;
        Updated?.Invoke(snapshot);
    }

    private async Task<IReadOnlyList<SensorReading>> ReadProviderAsync(ISensorProvider provider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gate = _providerGates.GetOrAdd(provider.Id, static _ => new SemaphoreSlim(1, 1));
        if (!gate.Wait(0))
        {
            if (_failingProviders.TryAdd(provider.Id, 0))
            {
                LogProviderBusy(provider.Id);
            }

            return [];
        }

        try
        {
            // Drivers block on HID I/O; Task.Run keeps providers parallel and lets the timeout fire.
            var readings = await Task.Run(async () =>
                {
                    try
                    {
                        return await provider.ReadSensorsAsync(cancellationToken);
                    }
                    finally
                    {
                        gate.Release();
                    }
                })
                .WaitAsync(options.Value.ProviderTimeout, cancellationToken);

            if (_failingProviders.TryRemove(provider.Id, out _))
            {
                LogProviderRecovered(provider.Id);
            }

            return readings;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Log once per failure streak to keep a flaky device from flooding the log at 1 Hz.
            if (_failingProviders.TryAdd(provider.Id, 0))
            {
                LogProviderFailed(ex, provider.Id);
            }

            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Previous sensor read from {Provider} is still running; skipping poll")]
    private partial void LogProviderBusy(string provider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading sensors from {Provider} failed; will keep retrying")]
    private partial void LogProviderFailed(Exception exception, string provider);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sensors from {Provider} are readable again")]
    private partial void LogProviderRecovered(string provider);
}
