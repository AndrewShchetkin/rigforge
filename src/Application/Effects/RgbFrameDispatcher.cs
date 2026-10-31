using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Application.Effects;

/// <summary>
/// Sends frames to one device on its own task with "latest frame wins" semantics, so a slow controller drops
/// frames instead of stalling the render loop. Identical frames are skipped except for a periodic keep-alive.
/// </summary>
internal sealed partial class RgbFrameDispatcher : IAsyncDisposable
{
    private readonly IRgbDevice _device;
    private readonly TimeSpan _keepAlive;
    private readonly ILogger _logger;
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private readonly Task _pump;

    private RgbColor[] _pending = [];
    private RgbColor[] _sending = [];
    private RgbColor[] _lastSent = [];
    private bool _failing;

    public RgbFrameDispatcher(IRgbDevice device, TimeSpan keepAlive, ILogger logger)
    {
        _device = device;
        _keepAlive = keepAlive;
        _logger = logger;
        _pump = Task.Run(PumpAsync);
    }

    public void Post(ReadOnlySpan<RgbColor> frame)
    {
        lock (_gate)
        {
            if (_pending.Length != frame.Length)
            {
                _pending = new RgbColor[frame.Length];
            }

            frame.CopyTo(_pending);
        }

        _signal.Writer.TryWrite(true);
    }

    public async ValueTask DisposeAsync()
    {
        _signal.Writer.TryComplete();
        await _stop.CancelAsync();
        try
        {
            await _pump;
        }
        catch (OperationCanceledException)
        {
        }

        _stop.Dispose();
    }

    private async Task PumpAsync()
    {
        var token = _stop.Token;
        var interval = TimeSpan.FromSeconds(1.0 / Math.Max(1, _device.MaxFramesPerSecond));
        var clock = Stopwatch.StartNew();
        TimeSpan? lastSendAt = null;

        while (await _signal.Reader.WaitToReadAsync(token))
        {
            _signal.Reader.TryRead(out _);

            lock (_gate)
            {
                (_pending, _sending) = (_sending, _pending);
                if (_pending.Length != _sending.Length)
                {
                    _pending = new RgbColor[_sending.Length];
                }
            }

            var now = clock.Elapsed;
            if (lastSendAt is { } sentAt)
            {
                if (_sending.AsSpan().SequenceEqual(_lastSent) && now - sentAt < _keepAlive)
                {
                    continue;
                }

                var wait = sentAt + interval - now;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, token);
                }
            }

            try
            {
                await _device.UpdateLedsAsync(_sending, token);
                lastSendAt = clock.Elapsed;
                if (_lastSent.Length != _sending.Length)
                {
                    _lastSent = new RgbColor[_sending.Length];
                }

                _sending.CopyTo(_lastSent, 0);
                if (_failing)
                {
                    _failing = false;
                    LogRecovered(_device.Id);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (!_failing)
                {
                    _failing = true;
                    LogSendFailed(ex, _device.Id);
                }

                // Back off a little so an unplugged device does not spin the CPU.
                await Task.Delay(TimeSpan.FromMilliseconds(500), token);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sending LED frame to {DeviceId} failed; retrying silently")]
    private partial void LogSendFailed(Exception exception, string deviceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "LED frames to {DeviceId} are flowing again")]
    private partial void LogRecovered(string deviceId);
}
