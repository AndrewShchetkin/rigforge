namespace RigForge.Infrastructure.CrossPlatform;

public sealed class HardwareOptions
{
    public const string SectionName = "Hardware";

    /// <summary>
    /// Detect devices and read sensors, but never send lighting, fan or LCD commands.
    /// Useful for a first run next to vendor software and for diagnostics.
    /// </summary>
    public bool DryRun { get; set; }

    public NzxtOptions Nzxt { get; set; } = new();

    public AsusOptions Asus { get; set; } = new();

    public NvidiaOptions Nvidia { get; set; } = new();

    public KingstonFuryOptions KingstonFury { get; set; } = new();

    public GpuRgbOptions GpuRgb { get; set; } = new();
}

public sealed class GpuRgbOptions
{
    public bool Enabled { get; set; } = true;
}

public sealed class NzxtOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Upload frames to the Kraken screen over USB bulk (WinUSB / libusb).</summary>
    public bool EnableLcd { get; set; } = true;

    /// <summary>Duty that controller fan channels (which have no temperature input) fall back to on shutdown.</summary>
    public int FallbackFanDutyPercent { get; set; } = 60;
}

public sealed class AsusOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>ARGB header length until the user sets it in the layout editor (the controller cannot detect it).</summary>
    public int DefaultArgbLedCount { get; set; } = 24;

    /// <summary>Additional Aura mainboard controller PIDs for boards newer than the built-in list.</summary>
    public int[] ExtraProductIds { get; set; } = [];
}

public sealed class NvidiaOptions
{
    public bool Enabled { get; set; } = true;
}

public sealed class KingstonFuryOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Upper bound of frames per second streamed over SMBus (hardware-paced at ~25-30 FPS for 2 DIMMs).</summary>
    public int MaxFramesPerSecond { get; set; } = 30;

    /// <summary>Controller master brightness register (0x20), 0..100 %.</summary>
    public int BrightnessPercent { get; set; } = 100;

    /// <summary>DIMM slot offsets from base address 0x60 to probe (default 0..3 = 0x60..0x63).</summary>
    public int[] CandidateSlots { get; set; } = [0, 1, 2, 3];

    /// <summary>
    /// Require a matched KF5* module (SMBIOS Type 17, or cached sysfs SPD / udev / EDAC) before opening SMBus.
    /// Unreadable DMI does not bypass this. Set false only to opt in to probing without module identity.
    /// </summary>
    public bool RequireSmbiosMatch { get; set; } = true;
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Directory for profile.json; empty = per-user application data folder.</summary>
    public string? DataDirectory { get; set; }
}
