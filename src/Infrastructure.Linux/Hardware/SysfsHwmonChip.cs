using System.Globalization;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Infrastructure.Linux.Hardware;

/// <summary>
/// One /sys/class/hwmon/hwmonN chip (k10temp, nct6799, nvme, ...): temp*_input, fan*_input and pwm* outputs.
/// Taking over a PWM output switches pwmN_enable to manual (1); restoring writes back the original mode
/// (e.g. 5 = SmartFan IV on Nuvoton chips).
/// </summary>
internal sealed class SysfsHwmonChip : ISensorProvider, IFanController
{
    private readonly string _path;
    private readonly bool _dryRun;
    private readonly Dictionary<string, (int Enable, int Duty)> _originalModes = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly List<(string File, string Id, string Name, SensorKind Kind, SensorRole Role)> _sensors = [];

    public SysfsHwmonChip(string path, string chipName, string id, bool dryRun)
    {
        _path = path;
        _dryRun = dryRun;
        Id = id;
        ChipName = chipName;

        foreach (var file in Directory.EnumerateFiles(path).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal))
        {
            if (TryParseInput(file, "temp", out var index))
            {
                var label = ReadLabel("temp", index) ?? $"Temperature {index}";
                _sensors.Add((file, $"{id}/temp{index}", label, SensorKind.Temperature, RoleFor(chipName, label)));
            }
            else if (TryParseInput(file, "fan", out index))
            {
                _sensors.Add((file, $"{id}/fan{index}", ReadLabel("fan", index) ?? $"Fan {index}", SensorKind.FanSpeed, SensorRole.None));
            }
        }

        Channels = Directory.EnumerateFiles(path, "pwm*_enable")
            .Select(f => Path.GetFileName(f)!.Replace("_enable", string.Empty, StringComparison.Ordinal))
            .Where(pwm => File.Exists(Path.Combine(path, pwm)))
            .Order(StringComparer.Ordinal)
            .Select(pwm =>
            {
                var number = pwm[3..];
                var fanLabel = ReadLabel("fan", int.TryParse(number, out var n) ? n : 0);
                var speedSensor = File.Exists(Path.Combine(path, $"fan{number}_input")) ? $"{id}/fan{number}" : null;
                return new FanChannel(pwm, fanLabel ?? $"PWM {number}", FanChannelKind.Fan, 0, 100, speedSensor);
            })
            .ToArray();

        Properties = new Dictionary<string, string> { ["Path"] = path, ["Chip"] = chipName };
    }

    public string Id { get; }

    public string ChipName { get; }

    public string Name => $"{ChipName} (hwmon)";

    public string Vendor => "Linux hwmon";

    public DeviceCapabilities Capabilities =>
        DeviceCapabilities.Sensors | (Channels.Count > 0 ? DeviceCapabilities.FanControl : DeviceCapabilities.None);

    public IReadOnlyDictionary<string, string> Properties { get; }

    public IReadOnlyList<FanChannel> Channels { get; }

    public bool HasSensors => _sensors.Count > 0;

    public ValueTask<IReadOnlyList<SensorReading>> ReadSensorsAsync(CancellationToken cancellationToken)
    {
        var readings = new List<SensorReading>(_sensors.Count);
        foreach (var (file, id, name, kind, role) in _sensors)
        {
            if (TryReadInt(file) is not { } raw)
            {
                continue;
            }

            var value = kind == SensorKind.Temperature ? raw / 1000f : raw;
            readings.Add(new SensorReading(id, name, kind, value) { Role = role });
        }

        foreach (var channel in Channels)
        {
            if (TryReadInt(channel.Id) is { } pwm)
            {
                readings.Add(new SensorReading($"{Id}/{channel.Id}", $"{channel.Name} duty", SensorKind.Duty, pwm * 100f / 255f));
            }
        }

        return ValueTask.FromResult<IReadOnlyList<SensorReading>>(readings);
    }

    public ValueTask SetDutyAsync(string channelId, int dutyPercent, CancellationToken cancellationToken)
    {
        EnsureChannel(channelId);
        if (_dryRun)
        {
            return ValueTask.CompletedTask;
        }

        lock (_gate)
        {
            if (!_originalModes.ContainsKey(channelId))
            {
                _originalModes[channelId] = (TryReadInt($"{channelId}_enable") ?? 2, TryReadInt(channelId) ?? 255);
            }

            Write($"{channelId}_enable", 1);
            Write(channelId, (int)Math.Round(Math.Clamp(dutyPercent, 0, 100) * 255 / 100.0));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RestoreDefaultAsync(string channelId, CancellationToken cancellationToken)
    {
        EnsureChannel(channelId);
        if (_dryRun)
        {
            return ValueTask.CompletedTask;
        }

        lock (_gate)
        {
            if (_originalModes.Remove(channelId, out var original))
            {
                Write($"{channelId}_enable", original.Enable);
                if (original.Enable == 1)
                {
                    Write(channelId, original.Duty);
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static SensorRole RoleFor(string chip, string label) => (chip, label) switch
    {
        ("k10temp" or "zenpower", "Tctl" or "Tdie") => SensorRole.CpuTemperature,
        ("coretemp", _) when label.StartsWith("Package id 0", StringComparison.Ordinal) => SensorRole.CpuTemperature,
        _ => SensorRole.None,
    };

    private static bool TryParseInput(string file, string prefix, out int index)
    {
        index = 0;
        return file.StartsWith(prefix, StringComparison.Ordinal)
            && file.EndsWith("_input", StringComparison.Ordinal)
            && int.TryParse(file.AsSpan(prefix.Length, file.Length - prefix.Length - "_input".Length), NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }

    private string? ReadLabel(string prefix, int index)
    {
        var file = Path.Combine(_path, $"{prefix}{index}_label");
        try
        {
            return File.Exists(file) ? File.ReadAllText(file).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private int? TryReadInt(string file)
    {
        try
        {
            return int.TryParse(File.ReadAllText(Path.Combine(_path, file)).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Some attributes return EIO/ENODATA while a sensor is idle or disconnected.
            return null;
        }
    }

    private void Write(string file, int value) =>
        File.WriteAllText(Path.Combine(_path, file), value.ToString(CultureInfo.InvariantCulture));

    private void EnsureChannel(string channelId)
    {
        if (!Channels.Any(c => c.Id == channelId))
        {
            throw new ArgumentException($"Unknown channel '{channelId}'.", nameof(channelId));
        }
    }
}
