using System.Globalization;
using System.Text.Json;
using HidSharp;
using Microsoft.Extensions.DependencyInjection;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Diagnostics;
using RigForge.Infrastructure.CrossPlatform.Hid;
using RigForge.Infrastructure.CrossPlatform.Kingston;
using RigForge.Infrastructure.CrossPlatform.Smbus;
using RigForge.Infrastructure.Linux.Udev;

namespace RigForge.Tools.HardwareScanner;

internal static class ProbeCommand
{
    public static async Task<int> RunAsync(bool verbose, CancellationToken cancellationToken)
    {
        await using var services = ScannerHost.Build(verbose);
        var devices = await ScannerHost.ProbeAllAsync(services, cancellationToken);
        try
        {
            foreach (var device in devices)
            {
                Print(device);
            }

            Console.WriteLine($"{devices.Count} device(s) detected (dry run: nothing was written to lighting, fans or screens).");
            return 0;
        }
        finally
        {
            await ScannerHost.ReleaseAsync(devices);
        }
    }

    private static void Print(IHardwareDevice device)
    {
        Console.WriteLine($"{device.Name}  [{device.Vendor}]  {device.Capabilities}");
        Console.WriteLine($"    id: {device.Id}");
        foreach (var (key, value) in device.Properties)
        {
            Console.WriteLine($"    {key}: {value}");
        }

        if (device is IRgbDevice rgb)
        {
            foreach (var zone in rgb.Zones)
            {
                Console.WriteLine($"    zone {zone.Id}: {zone.Name}, {zone.LedCount} LEDs ({zone.Topology}{(zone.IsResizable ? ", resizable" : string.Empty)})");
            }
        }

        if (device is IFanController fans)
        {
            foreach (var channel in fans.Channels)
            {
                Console.WriteLine($"    channel {channel.Id}: {channel.Name} ({channel.Kind}, {channel.MinDutyPercent}-{channel.MaxDutyPercent} %)");
            }
        }

        if (device is IAioLcdDevice lcd && device.Capabilities.HasFlag(DeviceCapabilities.Lcd))
        {
            Console.WriteLine($"    screen: {lcd.Lcd.Width}x{lcd.Lcd.Height}, up to {lcd.Lcd.MaxFramesPerSecond} FPS");
        }

        Console.WriteLine();
    }
}

internal static class SensorsCommand
{
    public static async Task<int> RunAsync(bool watch, bool verbose, CancellationToken cancellationToken)
    {
        await using var services = ScannerHost.Build(verbose);
        var devices = await ScannerHost.ProbeAllAsync(services, cancellationToken);
        try
        {
            do
            {
                var readings = await ReadAllAsync(devices, cancellationToken);
                if (watch && !Console.IsOutputRedirected)
                {
                    Console.Clear();
                }

                foreach (var reading in readings.OrderBy(r => r.Id, StringComparer.Ordinal))
                {
                    var role = reading.Role == SensorRole.None ? string.Empty : $"  <- {reading.Role}";
                    Console.WriteLine($"{reading.Id,-60} {reading.Value.ToString("0.0", CultureInfo.InvariantCulture),10} {reading.Unit,-4} {reading.Name}{role}");
                }

                Console.WriteLine($"{readings.Count} reading(s) at {DateTime.Now:T}");
                if (watch)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
            }
            while (watch && !cancellationToken.IsCancellationRequested);

            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        finally
        {
            await ScannerHost.ReleaseAsync(devices);
        }
    }

    public static async Task<IReadOnlyList<SensorReading>> ReadAllAsync(IEnumerable<IHardwareDevice> devices, CancellationToken cancellationToken)
    {
        var readings = new List<SensorReading>();
        foreach (var provider in devices.OfType<ISensorProvider>())
        {
            try
            {
                readings.AddRange(await provider.ReadSensorsAsync(cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await Console.Error.WriteLineAsync($"{provider.Name}: {ex.Message}");
            }
        }

        return readings;
    }
}

internal static class UdevCommand
{
    public static int Run(FileInfo? output)
    {
        var rules = UdevRulesGenerator.GenerateDefault();

        if (output is null)
        {
            Console.Write(rules);
            return 0;
        }

        File.WriteAllText(output.FullName, rules);
        Console.WriteLine($"Written {output.FullName}. Install it as {UdevRulesGenerator.RecommendedPath} and run: sudo udevadm control --reload-rules && sudo udevadm trigger");
        return 0;
    }
}

internal static class ConflictsCommand
{
    public static int Run()
    {
        var warnings = ConflictingSoftwareDetector.Detect();
        foreach (var warning in warnings)
        {
            Console.WriteLine($"! {warning}");
        }

        Console.WriteLine(warnings.Count == 0 ? "No conflicting software is running." : $"{warnings.Count} conflict(s) found.");
        return warnings.Count == 0 ? 0 : 2;
    }
}

internal static class SmbusCommand
{
    public static int Run(bool verbose)
    {
        using var services = ScannerHost.Build(verbose);
        var factory = services.GetRequiredService<ISmbusControllerFactory>();

        Console.WriteLine($"SMBus backend: {factory.BackendName} (available: {factory.IsAvailable})");
        Console.WriteLine($"Status:        {factory.DiagnosticStatus}");
        Console.WriteLine();

        var sticks = factory.GetInstalledMemory();
        Console.WriteLine($"SMBIOS Type 17 memory modules ({sticks.Count}):");
        foreach (var stick in sticks)
        {
            var tag = stick.IsKingstonFuryDdr5 ? " [Kingston FURY DDR5 match]" : string.Empty;
            Console.WriteLine($"  - {stick.DeviceLocator} ({stick.BankLocator}): {stick.Manufacturer} {stick.PartNumber}, {stick.CapacityMegabytes} MB @ {stick.SpeedMegaTransfers} MT/s{tag}");
        }

        Console.WriteLine();
        if (! sticks.Any(static s => s.IsKingstonFuryDdr5))
        {
            Console.WriteLine("No Kingston FURY DDR5 (KF5*) modules reported by SMBIOS; skipping SMBus scan (0 bytes sent on bus).");
            return 0;
        }

        if (!factory.IsAvailable)
        {
            Console.WriteLine("SMBus backend is not ready; run as Administrator with PawnIO installed to probe 0x60..0x63.");
            return 1;
        }

        var controllers = factory.OpenControllers();
        Console.WriteLine($"Opened {controllers.Count} SMBus controller(s) (firewall locked to 0x60..0x63, SPD 0x50..0x57 & PMIC 0x48..0x4F blocked):");
        try
        {
            foreach (var controller in controllers)
            {
                Console.WriteLine($"  Bus {controller.BusName}: {controller.Location}");
                for (var slot = 0; slot < 4; slot++)
                {
                    var result = KingstonFuryDramProtocol.ProbeSlot(controller, slot);
                    Console.WriteLine($"    slot {slot} (0x{result.Address:X2}): {result.Status} — {result.Detail}");
                }
            }
        }
        finally
        {
            foreach (var controller in controllers)
            {
                controller.Dispose();
            }
        }

        return 0;
    }
}

internal static class DumpCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> RunAsync(FileInfo? output, bool verbose, CancellationToken cancellationToken)
    {
        await using var services = ScannerHost.Build(verbose);
        var devices = await ScannerHost.ProbeAllAsync(services, cancellationToken);
        var manifests = services.GetRequiredService<IDeviceManifestCatalog>();
        var knownVendors = manifests.All.Select(static manifest => manifest.VendorId).ToHashSet();
        try
        {
            var report = new
            {
                GeneratedAt = DateTimeOffset.Now,
                Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                Runtime = Environment.Version.ToString(),
                Conflicts = ConflictingSoftwareDetector.Detect(),
                Hid = DeviceList.Local.GetHidDevices()
                    .Where(d => knownVendors.Contains(d.VendorID))
                    .Select(d => new
                    {
                        VendorId = $"{d.VendorID:X4}",
                        ProductId = $"{d.ProductID:X4}",
                        Product = d.SafeProductName(),
                        Manufacturer = d.SafeManufacturer(),
                        Support = manifests.FindUsb(d.VendorID, d.ProductID)?.Name ?? "not in the built-in manifest",
                        InputReport = d.GetMaxInputReportLength(),
                        OutputReport = d.GetMaxOutputReportLength(),
                        FeatureReport = d.GetMaxFeatureReportLength(),
                        Usages = d.SafeTopLevelUsages().Select(u => $"0x{u >> 16:X4}:0x{u & 0xFFFF:X4}").ToArray(),
                        Path = d.DevicePath,
                    })
                    .ToArray(),
                Devices = devices.Select(d => new
                {
                    d.Id,
                    d.Name,
                    d.Vendor,
                    Capabilities = d.Capabilities.ToString(),
                    d.Properties,
                    Zones = (d as IRgbDevice)?.Zones.Select(z => new { z.Id, z.Name, z.LedCount, Topology = z.Topology.ToString(), z.IsResizable }),
                    Channels = (d as IFanController)?.Channels.Select(c => new { c.Id, c.Name, Kind = c.Kind.ToString(), c.MinDutyPercent, c.MaxDutyPercent }),
                    Screen = (d as IAioLcdDevice)?.Lcd,
                }).ToArray(),
                Sensors = (await SensorsCommand.ReadAllAsync(devices, cancellationToken))
                    .Select(r => new { r.Id, r.Name, Kind = r.Kind.ToString(), Role = r.Role.ToString(), r.Value, r.Unit })
                    .ToArray(),
            };

            var json = JsonSerializer.Serialize(report, JsonOptions);
            if (output is null)
            {
                Console.WriteLine(json);
            }
            else
            {
                await File.WriteAllTextAsync(output.FullName, json, cancellationToken);
                Console.WriteLine($"Report written to {output.FullName}");
            }

            return 0;
        }
        finally
        {
            await ScannerHost.ReleaseAsync(devices);
        }
    }
}
