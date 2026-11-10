using System.Globalization;
using System.Net.Sockets;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.OpenRgb;

/// <summary>
/// One OpenRGB controller reached over the SDK socket.
/// <see cref="UpdateLedsAsync"/> fills a reusable buffer and sends it with no per-frame allocation.
/// </summary>
internal sealed class OpenRgbBridgeDevice : IRgbDevice
{
    private readonly OpenRgbLink _link;
    private readonly byte[] _send;
    private readonly uint _deviceIndex;
    private readonly int _ledCount;
    private readonly bool _dryRun;
    private readonly Lock _transaction = new();
    private bool _disposed;

    private OpenRgbBridgeDevice(OpenRgbLink link, uint deviceIndex, OpenRgbControllerInfo info, bool dryRun)
    {
        _link = link;
        _link.Adopt();
        _deviceIndex = deviceIndex;
        _ledCount = info.LedCount;
        _dryRun = dryRun;
        _send = new byte[OpenRgbProtocol.HeaderLength + (info.LedCount * 4)];
        Id = $"openrgb:{deviceIndex.ToString(CultureInfo.InvariantCulture)}:{Sanitize(info.Name)}";
        Name = info.Name;
        Vendor = info.Vendor.Length > 0 ? info.Vendor : "OpenRGB";
        Zones =
        [
            new RgbZone
            {
                Id = "leds",
                Name = info.ZoneCount > 1 ? $"{info.Name} ({info.ZoneCount} zones)" : info.Name,
                Topology = LedTopology.Linear,
                Leds = LedLayouts.Linear(info.LedCount),
                MaxLedCount = info.LedCount,
                IsResizable = false,
            },
        ];
        Properties = new Dictionary<string, string>
        {
            ["Endpoint"] = link.Endpoint,
            ["DeviceIndex"] = deviceIndex.ToString(CultureInfo.InvariantCulture),
            ["Location"] = info.Location,
            ["Serial"] = info.Serial,
            ["Version"] = info.Version,
        };
    }

    public string Id { get; }

    public string Name { get; }

    public string Vendor { get; }

    public DeviceCapabilities Capabilities => DeviceCapabilities.Rgb;

    public IReadOnlyDictionary<string, string> Properties { get; }

    public IReadOnlyList<RgbZone> Zones { get; }

    public int MaxFramesPerSecond => 60;

    public static async Task<IReadOnlyList<OpenRgbBridgeDevice>> ConnectAllAsync(
        string host,
        int port,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(800));
        var client = new TcpClient { NoDelay = true };
        OpenRgbLink? link = null;
        try
        {
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            client.ReceiveTimeout = 1500;
            client.SendTimeout = 1500;
            link = new OpenRgbLink(client, $"{host}:{port.ToString(CultureInfo.InvariantCulture)}");
            var socket = client.Client;
            if (!Handshake(socket, out var controllers))
            {
                link.Dispose();
                return [];
            }

            var devices = new List<OpenRgbBridgeDevice>(controllers.Count);
            var customMode = new byte[OpenRgbProtocol.HeaderLength];
            try
            {
                foreach (var (index, info) in controllers)
                {
                    var device = new OpenRgbBridgeDevice(link, index, info, dryRun);
                    devices.Add(device);
                    if (!dryRun)
                    {
                        OpenRgbProtocol.WriteSimple(customMode, index, OpenRgbProtocol.SetCustomMode);
                        lock (link.Gate)
                        {
                            SendAll(socket, customMode);
                        }
                    }
                }
            }
            catch
            {
                foreach (var device in devices)
                {
                    device.Dispose();
                }

                if (devices.Count == 0)
                {
                    link.Dispose();
                }

                throw;
            }

            if (devices.Count == 0)
            {
                link.Dispose();
            }

            return devices;
        }
        catch
        {
            if (link is null)
            {
                client.Dispose();
            }
            else if (!link.HasReferences)
            {
                link.Dispose();
            }

            throw;
        }
    }

    public bool TryResizeZone(string zoneId, int ledCount) => false;

    public ValueTask UpdateLedsAsync(ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_dryRun || colors.IsEmpty || _ledCount == 0)
        {
            return ValueTask.CompletedTask;
        }

        lock (_transaction)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var count = Math.Min(_ledCount, colors.Length);
            var written = OpenRgbProtocol.WriteUpdateLeds(_deviceIndex, colors.Span[..count], _send);
            lock (_link.Gate)
            {
                SendAll(_link.Socket, _send.AsSpan(0, written));
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        lock (_transaction)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _link.Release();
        }
    }

    private static bool Handshake(Socket socket, out List<(uint Index, OpenRgbControllerInfo Info)> controllers)
    {
        controllers = [];
        Span<byte> request = stackalloc byte[64];
        var versionLength = OpenRgbProtocol.WriteProtocolVersionRequest(request);
        SendAll(socket, request[..versionLength]);
        if (!TryRead(socket, out var packetId, out var body) || packetId != OpenRgbProtocol.RequestProtocolVersion || body.Length < 4)
        {
            return false;
        }

        var nameLength = OpenRgbProtocol.WriteClientName(request);
        SendAll(socket, request[..nameLength]);

        var countLength = OpenRgbProtocol.WriteSimple(request, 0, OpenRgbProtocol.RequestControllerCount);
        SendAll(socket, request[..countLength]);
        if (!TryRead(socket, out packetId, out body) || packetId != OpenRgbProtocol.RequestControllerCount || body.Length < 4)
        {
            return false;
        }

        var count = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body);
        if (count > OpenRgbProtocol.MaxControllers)
        {
            count = OpenRgbProtocol.MaxControllers;
        }

        for (uint index = 0; index < count; index++)
        {
            var dataLength = OpenRgbProtocol.WriteSimple(request, index, OpenRgbProtocol.RequestControllerData);
            SendAll(socket, request[..dataLength]);
            if (!TryRead(socket, out packetId, out body) || packetId != OpenRgbProtocol.RequestControllerData)
            {
                continue;
            }

            if (OpenRgbProtocol.TryParseControllerData(body, out var info))
            {
                controllers.Add((index, info));
            }
        }

        return true;
    }

    private static bool TryRead(Socket socket, out uint packetId, out byte[] body)
    {
        packetId = 0;
        body = [];
        Span<byte> header = stackalloc byte[OpenRgbProtocol.HeaderLength];
        if (!ReadExact(socket, header) || !OpenRgbProtocol.TryParseHeader(header, out _, out packetId, out var length))
        {
            return false;
        }

        body = length == 0 ? [] : new byte[length];
        return length == 0 || ReadExact(socket, body);
    }

    private static void SendAll(Socket socket, ReadOnlySpan<byte> data)
    {
        var sent = 0;
        while (sent < data.Length)
        {
            var count = socket.Send(data[sent..], SocketFlags.None);
            if (count <= 0)
            {
                throw new IOException("OpenRGB socket closed while sending.");
            }

            sent += count;
        }
    }

    private static bool ReadExact(Socket socket, Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = socket.Receive(buffer[read..], SocketFlags.None);
            if (count <= 0)
            {
                return false;
            }

            read += count;
        }

        return true;
    }

    private static string Sanitize(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        foreach (var ch in value)
        {
            buffer[length++] = char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-';
        }

        var text = new string(buffer[..length]).Trim('-');
        return text.Length == 0 ? "device" : text;
    }

    internal sealed class OpenRgbLink(TcpClient client, string endpoint) : IDisposable
    {
        private int _refs;
        private bool _disposed;

        public string Endpoint { get; } = endpoint;

        public Socket Socket => client.Client;

        public Lock Gate { get; } = new();

        public bool HasReferences => Volatile.Read(ref _refs) > 0;

        public void Adopt() => Interlocked.Increment(ref _refs);

        public void Release()
        {
            if (Interlocked.Decrement(ref _refs) == 0)
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            client.Dispose();
        }
    }
}
