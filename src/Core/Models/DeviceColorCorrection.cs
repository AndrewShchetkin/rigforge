namespace RigForge.Core.Models;

/// <summary>
/// Per-device gamma and white balance applied to every LED color after the effect is sampled.
/// Gamma 1 and white balance #FFFFFF leave the color unchanged apart from global brightness.
/// </summary>
public sealed record DeviceColorCorrection
{
    public required string DeviceId { get; init; }

    /// <summary>Gamma applied to the brightness-scaled channel, 0.5..3.</summary>
    public float Gamma { get; init; } = 1f;

    /// <summary>Per-channel gain as #RRGGBB. #FFFFFF is neutral.</summary>
    public string WhiteBalance { get; init; } = "#FFFFFF";
}
