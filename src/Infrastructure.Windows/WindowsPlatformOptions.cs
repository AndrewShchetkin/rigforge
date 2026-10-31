namespace RigForge.Infrastructure.Windows;

public sealed class WindowsPlatformOptions
{
    public const string SectionName = "Windows";

    /// <summary>
    /// CPU temperatures and the Super I/O chip need ring-0 access. LibreHardwareMonitor 0.9.5+ uses the signed
    /// PawnIO driver instead of WinRing0; it requires administrator rights and the PawnIO driver installed.
    /// </summary>
    public bool EnableLibreHardwareMonitor { get; set; } = true;

    /// <summary>Expose the Super I/O PWM outputs (motherboard fan headers) as controllable channels.</summary>
    public bool EnableMotherboardFanControl { get; set; } = true;

    public DxgiCaptureOptions Capture { get; set; } = new();
}

public sealed class DxgiCaptureOptions
{
    /// <summary>Index of the monitor across all adapters.</summary>
    public int OutputIndex { get; set; }

    /// <summary>Width the GPU downscales to (via mip maps) before the frame is copied to the CPU.</summary>
    public int TargetWidth { get; set; } = 160;

    public int MaxFramesPerSecond { get; set; } = 60;
}
