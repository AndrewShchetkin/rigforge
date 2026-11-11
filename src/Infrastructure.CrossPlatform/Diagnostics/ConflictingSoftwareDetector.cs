using System.Diagnostics;

namespace RigForge.Infrastructure.CrossPlatform.Diagnostics;

/// <summary>Finds running vendor tools that talk to the same controllers and would fight over them.</summary>
public static class ConflictingSoftwareDetector
{
    private static readonly (string ProcessName, string Product, string Impact)[] KnownConflicts =
    [
        ("NZXT CAM", "NZXT CAM", "locks the Kraken screen and overrides pump, fan and RGB settings"),
        ("SignalRgb", "SignalRGB", "streams RGB frames to the same controllers"),
        ("LightingService", "ASUS Armoury Crate (LightingService)", "overrides Aura motherboard lighting"),
        ("ArmouryCrate", "ASUS Armoury Crate", "overrides Aura motherboard lighting"),
        ("FURYCTRL", "Kingston FURY CTRL", "writes to Kingston FURY DRAM RGB registers over SMBus"),
        ("SignalRgbService", "SignalRGB Service", "holds hardware lighting buses in the background"),
        ("RGBFusion", "Gigabyte RGB Fusion", "overrides Gigabyte motherboard and GPU lighting"),
        ("OpenRGB", "OpenRGB", "streams RGB frames to the same controllers"),
        ("FanControl", "FanControl", "writes fan duties to the same channels"),
        ("liquidctl", "liquidctl", "sends commands to the same NZXT devices"),
    ];

    public static IReadOnlyList<string> Detect()
    {
        var warnings = new List<string>();
        foreach (var (processName, product, impact) in KnownConflicts)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            if (processes.Length > 0)
            {
                warnings.Add($"{product} is running: it {impact}. Close it while RigForge controls your hardware.");
            }

            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        return warnings;
    }
}
