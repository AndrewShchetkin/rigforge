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
        DetectMultipleRigForgeInstances(warnings);
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

    internal static string? FormatDuplicateDaemonWarning(int currentPid, ReadOnlySpan<int> daemonPids)
    {
        var otherCount = 0;
        var firstOtherPid = 0;
        foreach (var pid in daemonPids)
        {
            if (pid != currentPid)
            {
                if (otherCount == 0)
                {
                    firstOtherPid = pid;
                }

                otherCount++;
            }
        }

        return otherCount switch
        {
            0 => null,
            1 => $"Another RigForge.Daemon instance (PID {firstOtherPid}) is running: multiple instances stream conflicting RGB frames and cause flickering. Stop the duplicate process.",
            _ => $"{otherCount} other RigForge.Daemon instances (including PID {firstOtherPid}) are running: multiple instances stream conflicting RGB frames and cause flickering. Stop the duplicate processes.",
        };
    }

    private static void DetectMultipleRigForgeInstances(List<string> warnings)
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName("RigForge.Daemon");
        }
        catch (InvalidOperationException)
        {
            return;
        }

        try
        {
            // In rigforge-scan (where the current process is not RigForge.Daemon), warn only when at least two daemons run.
            // inside RigForge.Daemon itself, any other RigForge.Daemon PID != Environment.ProcessId is a duplicate.
            var currentPid = Environment.ProcessId;
            var isDaemonProcess = processes.Any(p => p.Id == currentPid);
            if (!isDaemonProcess && processes.Length <= 1)
            {
                return;
            }

            Span<int> pids = processes.Length <= 16 ? stackalloc int[processes.Length] : new int[processes.Length];
            for (var i = 0; i < processes.Length; i++)
            {
                pids[i] = processes[i].Id;
            }

            var effectiveSelf = isDaemonProcess ? currentPid : pids[0];
            if (FormatDuplicateDaemonWarning(effectiveSelf, pids) is { } message)
            {
                warnings.Add(message);
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}
