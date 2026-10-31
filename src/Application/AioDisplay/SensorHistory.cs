namespace RigForge.Application.AioDisplay;

/// <summary>Fixed-size ring buffer of samples for the LCD graph mode. Thread-safe.</summary>
public sealed class SensorHistory(int capacity)
{
    private readonly float[] _samples = new float[Math.Max(2, capacity)];
    private readonly Lock _gate = new();
    private int _start;
    private int _count;

    public string? Selector { get; private set; }

    public void Record(string selector, float value)
    {
        lock (_gate)
        {
            if (!string.Equals(selector, Selector, StringComparison.Ordinal))
            {
                Selector = selector;
                _start = 0;
                _count = 0;
            }

            var index = (_start + _count) % _samples.Length;
            _samples[index] = value;
            if (_count < _samples.Length)
            {
                _count++;
            }
            else
            {
                _start = (_start + 1) % _samples.Length;
            }
        }
    }

    /// <summary>Copies the samples, oldest first.</summary>
    public float[] Snapshot()
    {
        lock (_gate)
        {
            var result = new float[_count];
            for (var i = 0; i < _count; i++)
            {
                result[i] = _samples[(_start + i) % _samples.Length];
            }

            return result;
        }
    }
}
