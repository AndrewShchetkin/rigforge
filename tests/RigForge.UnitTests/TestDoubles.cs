using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.UnitTests;

internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}

internal sealed class FakeRgbDevice(string id, params RgbZone[] zones) : IRgbDevice
{
    public string Id => id;

    public string Name => id;

    public string Vendor => "Test";

    public DeviceCapabilities Capabilities => DeviceCapabilities.Rgb;

    public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>();

    public IReadOnlyList<RgbZone> Zones { get; private set; } = zones;

    public int MaxFramesPerSecond => 60;

    public bool TryResizeZone(string zoneId, int ledCount)
    {
        Zones = Zones.Select(z => z.Id == zoneId ? z with { Leds = LedLayouts.Linear(ledCount) } : z).ToArray();
        return true;
    }

    public ValueTask UpdateLedsAsync(ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeFanController(string id, params FanChannel[] channels) : IFanController, ISensorProvider
{
    public Dictionary<string, int> Duties { get; } = [];

    public List<string> Restored { get; } = [];

    public List<SensorReading> Readings { get; } = [];

    public string Id => id;

    public string Name => id;

    public string Vendor => "Test";

    public DeviceCapabilities Capabilities => DeviceCapabilities.FanControl | DeviceCapabilities.Sensors;

    public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>();

    public IReadOnlyList<FanChannel> Channels => channels;

    public ValueTask SetDutyAsync(string channelId, int dutyPercent, CancellationToken cancellationToken)
    {
        Duties[channelId] = dutyPercent;
        return ValueTask.CompletedTask;
    }

    public ValueTask RestoreDefaultAsync(string channelId, CancellationToken cancellationToken)
    {
        Restored.Add(channelId);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<SensorReading>> ReadSensorsAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<SensorReading>>(Readings.ToArray());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class StaticProbe(params IHardwareDevice[] devices) : IDeviceProbe
{
    public string Name => "static";

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<IHardwareDevice>>(devices);
}

internal sealed class InMemoryProfileStore : IProfileStore
{
    public RigProfile? Saved { get; private set; }

    public Task<RigProfile?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Saved);

    public Task SaveAsync(RigProfile profile, CancellationToken cancellationToken)
    {
        Saved = profile;
        return Task.CompletedTask;
    }
}

internal static class Approx
{
    public static void Equal(float expected, float actual, float tolerance = 0.01f) =>
        Assert.True(MathF.Abs(expected - actual) <= tolerance, $"Expected {expected} ± {tolerance}, got {actual}.");
}
