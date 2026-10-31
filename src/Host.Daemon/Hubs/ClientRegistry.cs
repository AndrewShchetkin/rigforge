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
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<PreviewChannel, byte>> _clients = new(StringComparer.Ordinal);

    public int ClientCount => _clients.Count;

    public static string GroupName(PreviewChannel channel) => $"preview:{channel}";

    public bool HasSubscribers(PreviewChannel channel) => _clients.Values.Any(c => c.ContainsKey(channel));

    public void Connected(string connectionId) => _clients.TryAdd(connectionId, new ConcurrentDictionary<PreviewChannel, byte>());

    public void Disconnected(string connectionId)
    {
        _clients.TryRemove(connectionId, out _);
        UpdateEngines();
    }

    public void SetPreview(string connectionId, PreviewChannel channel, bool enabled)
    {
        var subscriptions = _clients.GetOrAdd(connectionId, _ => new ConcurrentDictionary<PreviewChannel, byte>());
        if (enabled)
        {
            subscriptions.TryAdd(channel, 0);
        }
        else
        {
            subscriptions.TryRemove(channel, out _);
        }

        UpdateEngines();
    }

    private void UpdateEngines()
    {
        effects.PreviewRequested = HasSubscribers(PreviewChannel.Leds);
        lcd.PreviewRequested = HasSubscribers(PreviewChannel.Lcd);
    }
}
