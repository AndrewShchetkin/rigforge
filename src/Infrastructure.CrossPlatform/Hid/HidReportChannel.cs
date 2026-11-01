using HidSharp;

namespace RigForge.Infrastructure.CrossPlatform.Hid;

/// <summary>
/// Thread-safe wrapper around a HidSharp stream that speaks in whole reports.
/// Byte 0 of every report is the report ID (NZXT uses the command byte as report ID, ASUS Aura uses 0xEC).
/// </summary>
internal sealed class HidReportChannel : IDisposable
{
    private const int DefaultTimeoutMs = 500;

    private readonly Stream _stream;
    private readonly byte[] _output;
    private readonly byte[] _input;
    private readonly Lock _writeGate = new();
    private readonly Lock _readGate = new();
    private volatile bool _disposed;

    private HidReportChannel(HidDevice device, HidStream stream)
        : this(stream, device.GetMaxOutputReportLength(), device.GetMaxInputReportLength())
    {
        Device = device;
    }

    internal HidReportChannel(Stream stream, int outputReportLength = 64, int inputReportLength = 64)
    {
        Device = null!;
        _stream = stream;
        _output = new byte[Math.Max(outputReportLength, 1)];
        _input = new byte[Math.Max(inputReportLength, 1)];
    }

    public HidDevice Device { get; }

    public static HidReportChannel Open(HidDevice device)
    {
        var stream = device.Open();
        stream.WriteTimeout = 1000;
        stream.ReadTimeout = DefaultTimeoutMs;
        return new HidReportChannel(device, stream);
    }

    /// <summary>Sends one output report, zero-padded to the device's report length.</summary>
    public void Write(ReadOnlySpan<byte> report)
    {
        lock (_writeGate)
        {
            WriteCore(report);
        }
    }

    /// <summary>Sends a request and returns the first input report accepted by <paramref name="accept"/>, or null on timeout.</summary>
    public byte[]? Request(ReadOnlySpan<byte> request, Func<byte[], bool> accept, int timeoutMs = DefaultTimeoutMs)
    {
        lock (_readGate)
        {
            lock (_writeGate)
            {
                WriteCore(request);
            }

            return ReadUntilCore(accept, timeoutMs);
        }
    }

    public byte[]? ReadUntil(Func<byte[], bool> accept, int timeoutMs = DefaultTimeoutMs)
    {
        lock (_readGate)
        {
            return ReadUntilCore(accept, timeoutMs);
        }
    }

    /// <summary>Drops queued input reports; devices that stream status would otherwise hand out stale data.</summary>
    public void DrainInput()
    {
        lock (_readGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _stream.ReadTimeout = 1;
            for (var i = 0; i < 256; i++)
            {
                try
                {
                    if (_stream.Read(_input, 0, _input.Length) <= 0)
                    {
                        break;
                    }
                }
                catch (TimeoutException)
                {
                    break;
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_readGate)
        {
            lock (_writeGate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _stream.Dispose();
            }
        }
    }

    private void WriteCore(ReadOnlySpan<byte> report)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Array.Clear(_output);
        report[..Math.Min(report.Length, _output.Length)].CopyTo(_output);
        _stream.Write(_output, 0, _output.Length);
    }

    private byte[]? ReadUntilCore(Func<byte[], bool> accept, int timeoutMs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            var remaining = deadline - Environment.TickCount64;
            if (remaining <= 0)
            {
                return null;
            }

            _stream.ReadTimeout = (int)remaining;
            int count;
            try
            {
                count = _stream.Read(_input, 0, _input.Length);
            }
            catch (TimeoutException)
            {
                return null;
            }

            if (count <= 0)
            {
                continue;
            }

            if (count < _input.Length)
            {
                _input.AsSpan(count).Clear();
            }

            if (accept(_input))
            {
                return _input.AsSpan(0, count).ToArray();
            }
        }
    }
}
