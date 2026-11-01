using System.Diagnostics.CodeAnalysis;

namespace RigForge.Core.Models;

/// <summary>Immutable view of the latest value of every known sensor.</summary>
public sealed class TelemetrySnapshot
{
    /// <summary>Selector prefix that resolves a sensor by <see cref="SensorRole"/> instead of by id.</summary>
    public const string RolePrefix = "role:";

    private readonly Dictionary<string, SensorReading> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<SensorRole, SensorReading> _byRole = [];

    public TelemetrySnapshot(DateTimeOffset timestamp, IEnumerable<SensorReading> readings)
    {
        Timestamp = timestamp;

        // Ordering makes role resolution deterministic when several sensors claim the same role.
        foreach (var reading in readings.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            _byId[reading.Id] = reading;
            if (reading.Role == SensorRole.None)
            {
                continue;
            }

            if (IsTemperatureRole(reading.Role))
            {
                if (!float.IsFinite(reading.Value))
                {
                    continue;
                }

                if (!_byRole.TryGetValue(reading.Role, out var current) || reading.Value > current.Value)
                {
                    _byRole[reading.Role] = reading;
                }
            }
            else
            {
                _byRole.TryAdd(reading.Role, reading);
            }
        }
    }

    private static bool IsTemperatureRole(SensorRole role) =>
        role is SensorRole.CpuTemperature or SensorRole.GpuTemperature or SensorRole.LiquidTemperature;

    public static TelemetrySnapshot Empty { get; } = new(DateTimeOffset.MinValue, []);

    public DateTimeOffset Timestamp { get; }

    public IReadOnlyCollection<SensorReading> Readings => _byId.Values;

    public static string RoleSelector(SensorRole role) => RolePrefix + role;

    /// <summary>Resolves "role:&lt;SensorRole&gt;" or an exact sensor id.</summary>
    public bool TryResolve(string? selector, [NotNullWhen(true)] out SensorReading? reading)
    {
        reading = null;
        if (string.IsNullOrWhiteSpace(selector))
        {
            return false;
        }

        if (selector.StartsWith(RolePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Enum.TryParse<SensorRole>(selector.AsSpan(RolePrefix.Length), ignoreCase: true, out var role)
                && _byRole.TryGetValue(role, out reading);
        }

        return _byId.TryGetValue(selector, out reading);
    }
}
