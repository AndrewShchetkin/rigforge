using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;

namespace RigForge.Infrastructure.CrossPlatform.Nvidia;

internal sealed partial class NvmlProbe(IOptions<HardwareOptions> options, ILogger<NvmlProbe> logger) : IDeviceProbe
{
    public string Name => "NVIDIA NVML";

    public Task<IReadOnlyList<IHardwareDevice>> ProbeAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Nvidia.Enabled)
        {
            return Task.FromResult<IReadOnlyList<IHardwareDevice>>([]);
        }

        var nvml = NvmlLibrary.TryLoad(out var error);
        if (nvml is null)
        {
            LogUnavailable(error ?? "unknown error");
            return Task.FromResult<IReadOnlyList<IHardwareDevice>>([]);
        }

        var devices = new List<IHardwareDevice>();
        for (uint index = 0; index < nvml.DeviceCount; index++)
        {
            var handle = nvml.GetHandle(index);
            if (handle != 0)
            {
                devices.Add(new NvmlGpuDevice(nvml.AddReference(), handle, index));
            }
        }

        // Each device holds its own reference; drop the probe's.
        nvml.Dispose();
        return Task.FromResult<IReadOnlyList<IHardwareDevice>>(devices);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "NVIDIA telemetry unavailable: {Reason}")]
    private partial void LogUnavailable(string reason);
}
