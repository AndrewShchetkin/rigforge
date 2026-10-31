using RigForge.Application.AioDisplay;
using RigForge.Application.Cooling;
using RigForge.Application.Effects;
using RigForge.Application.Telemetry;

namespace RigForge.Host.Daemon.Services;

/// <summary>
/// Hosts an engine loop and restarts it after unexpected failures instead of stopping the whole daemon:
/// a bug in, say, the LCD renderer must never take fan control down with it.
/// </summary>
internal abstract partial class ResilientWorker(ILogger logger) : BackgroundService
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(2);

    protected abstract string LoopName { get; }

    protected abstract Task RunAsync(CancellationToken stoppingToken);

    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogCrashed(ex, LoopName);
                await Task.Delay(RestartDelay, stoppingToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Loop} loop crashed; restarting in 2 s")]
    private partial void LogCrashed(Exception exception, string loop);
}

internal sealed class TelemetryWorker(TelemetryService telemetry, ILogger<TelemetryWorker> logger) : ResilientWorker(logger)
{
    protected override string LoopName => "Telemetry";

    protected override Task RunAsync(CancellationToken stoppingToken) => telemetry.RunAsync(stoppingToken);
}

internal sealed class FanControlWorker(FanControlEngine engine, ILogger<FanControlWorker> logger) : ResilientWorker(logger)
{
    protected override string LoopName => "Fan control";

    protected override Task RunAsync(CancellationToken stoppingToken) => engine.RunAsync(stoppingToken);
}

internal sealed class EffectRenderWorker(EffectRenderEngine engine, ILogger<EffectRenderWorker> logger) : ResilientWorker(logger)
{
    protected override string LoopName => "Effect rendering";

    protected override Task RunAsync(CancellationToken stoppingToken) => engine.RunAsync(stoppingToken);
}

internal sealed class LcdRenderWorker(LcdRenderEngine engine, ILogger<LcdRenderWorker> logger) : ResilientWorker(logger)
{
    protected override string LoopName => "LCD rendering";

    protected override Task RunAsync(CancellationToken stoppingToken) => engine.RunAsync(stoppingToken);
}
