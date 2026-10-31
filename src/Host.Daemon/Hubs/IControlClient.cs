using RigForge.Host.Daemon.Contracts;

namespace RigForge.Host.Daemon.Hubs;

/// <summary>Server → client events of <see cref="ControlHub"/>.</summary>
public interface IControlClient
{
    Task Telemetry(TelemetryDto telemetry);

    Task FanStatus(IReadOnlyList<FanStatusDto> status);

    Task LedPreview(LedPreviewDto frame);

    Task LcdPreview(string pngBase64);

    Task StateChanged(SystemStateDto state);
}
