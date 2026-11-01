using System.Collections.Concurrent;
using RigForge.Application.AioDisplay;
using RigForge.Application.Effects;
using RigForge.Host.Daemon.Contracts;

namespace RigForge.Host.Daemon.Hubs;

/// <summary>
/// Tracks connected UI clients and their preview subscriptions. Previews are only rendered and sent while someone
/// watches, which keeps the daemon idle (CPU and allocations) when the UI is closed.
/// </summary>
public sealed class ClientRegistry(EffectRenderEngine effects, LcdRenderEngine lcd)
{
    private static readonly AsyncLocal<string?> CurrentMutationCaller = new();

    private readonly Lock _gate = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<PreviewChannel, byte>> _clients = new(StringComparer.Ordinal);
    private volatile bool _hasLedSubscribers;
    private volatile bool _hasLcdSubscribers;

    public event Action? SubscriptionsChanged;

    public int ClientCount => _clients.Count;

    public string? ActiveMutationConnectionId => CurrentMutationCaller.Value;

    public static string GroupName(PreviewChannel channel) => $"preview:{channel}";

    public bool HasSubscribers(PreviewChannel channel) => channel switch
    {
        PreviewChannel.Leds => _hasLedSubscribers,
        PreviewChannel.Lcd => _hasLcdSubscribers,
        _ => false,
    };

    public IDisposable BeginMutationScope(string connectionId)
    {
        var previous = CurrentMutationCaller.Value;
        CurrentMutationCaller.Value = connectionId;
        return new MutationScope(previous);
    }

    public void Connected(string connectionId)
    {
        lock (_gate)
        {
            _clients.TryAdd(connectionId, new ConcurrentDictionary<PreviewChannel, byte>());
        }
    }

    public void Disconnected(string connectionId)
    {
        bool changed;
        lock (_gate)
        {
            _clients.TryRemove(connectionId, out _);
            changed = UpdateEngines();
        }

        if (changed)
        {
            SubscriptionsChanged?.Invoke();
        }
    }

    public void SetPreview(string connectionId, PreviewChannel channel, bool enabled)
    {
        bool changed;
        lock (_gate)
        {
            if (!_clients.TryGetValue(connectionId, out var subscriptions))
            {
                return;
            }

            if (enabled)
            {
                subscriptions.TryAdd(channel, 0);
            }
            else
            {
                subscriptions.TryRemove(channel, out _);
            }

            changed = UpdateEngines();
        }

        if (changed)
        {
            SubscriptionsChanged?.Invoke();
        }
    }

    private bool UpdateEngines()
    {
        var hasLeds = _clients.Values.Any(c => c.ContainsKey(PreviewChannel.Leds));
        var hasLcd = _clients.Values.Any(c => c.ContainsKey(PreviewChannel.Lcd));
        var changed = _hasLedSubscribers != hasLeds || _hasLcdSubscribers != hasLcd;
        _hasLedSubscribers = hasLeds;
        _hasLcdSubscribers = hasLcd;
        effects.PreviewRequested = hasLeds;
        lcd.PreviewRequested = hasLcd;
        return changed;
    }

    private sealed class MutationScope(string? previous) : IDisposable
    {
        public void Dispose() => CurrentMutationCaller.Value = previous;
    }
}
