using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.Nvidia;

/// <summary>Telemetry of one NVIDIA GPU through NVML (user mode, no kernel driver of our own).</summary>
internal sealed class NvmlGpuDevice : ISensorProvider
{
    private readonly NvmlLibrary _nvml;
    private readonly nint _handle;
    private readonly bool _primary;
    private readonly Dictionary<string, string> _properties;
    private int _disposed;

    public NvmlGpuDevice(NvmlLibrary nvml, nint handle, uint index)
    {
        _nvml = nvml;
        _handle = handle;
        _primary = index == 0;

        var uuid = nvml.GetUuid(handle);
        Id = $"nvml:{(string.IsNullOrEmpty(uuid) ? index.ToString(System.Globalization.CultureInfo.InvariantCulture) : uuid)}";
        Name = nvml.GetName(handle) is { Length: > 0 } name ? name : $"NVIDIA GPU {index}";
        _properties = new Dictionary<string, string> { ["Index"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture), ["Uuid"] = uuid };
    }

    public string Id { get; }

    public string Name { get; }

    public string Vendor => "NVIDIA";

    public DeviceCapabilities Capabilities => DeviceCapabilities.Sensors;

    public IReadOnlyDictionary<string, string> Properties => _properties;

    public ValueTask<IReadOnlyList<SensorReading>> ReadSensorsAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var readings = new List<SensorReading>(8);

        if (_nvml.GetTemperature(_handle) is { } temperature)
        {
            readings.Add(new SensorReading($"{Id}/temperature", "GPU temperature", SensorKind.Temperature, temperature)
            {
                Role = _primary ? SensorRole.GpuTemperature : SensorRole.None,
            });
        }

        if (_nvml.GetUtilization(_handle) is { } utilization)
        {
            readings.Add(new SensorReading($"{Id}/load", "GPU load", SensorKind.Load, utilization.Gpu)
            {
                Role = _primary ? SensorRole.GpuLoad : SensorRole.None,
            });
            readings.Add(new SensorReading($"{Id}/memory-controller-load", "Memory controller load", SensorKind.Load, utilization.Memory));
        }

        if (_nvml.GetPowerMilliwatts(_handle) is { } milliwatts)
        {
            readings.Add(new SensorReading($"{Id}/power", "GPU power", SensorKind.Power, milliwatts / 1000f)
            {
                Role = _primary ? SensorRole.GpuPower : SensorRole.None,
            });
        }

        if (_nvml.GetFanSpeedPercent(_handle) is { } fan)
        {
            readings.Add(new SensorReading($"{Id}/fan", "GPU fan", SensorKind.Duty, fan));
        }

        if (_nvml.GetGraphicsClock(_handle) is { } coreClock)
        {
            readings.Add(new SensorReading($"{Id}/core-clock", "GPU core clock", SensorKind.Clock, coreClock));
        }

        if (_nvml.GetMemoryClock(_handle) is { } memoryClock)
        {
            readings.Add(new SensorReading($"{Id}/memory-clock", "GPU memory clock", SensorKind.Clock, memoryClock));
        }

        if (_nvml.GetUsedMemoryBytes(_handle) is { } used)
        {
            readings.Add(new SensorReading($"{Id}/vram-used", "VRAM used", SensorKind.Data, used / (1024f * 1024f)));
        }

        return ValueTask.FromResult<IReadOnlyList<SensorReading>>(readings);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _nvml.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
