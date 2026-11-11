using Microsoft.Extensions.DependencyInjection;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Gpu;
using RigForge.Infrastructure.CrossPlatform.Gpu.Asus;

namespace RigForge.Tools.HardwareScanner;

/// <summary>
/// Read-only GPU I2C lighting controller diagnostic. For ASUS Aura cards it reads the controller name and LED count;
/// no mode, color or save command is ever sent.
/// </summary>
internal static class GpuI2cCommand
{
    public static int Run(bool verbose)
    {
        using var services = ScannerHost.Build(verbose);
        var factory = services.GetRequiredService<IGpuI2cBusFactory>();
        var manifests = services.GetService<IDeviceManifestCatalog>();

        Console.WriteLine($"GPU I2C backend: {factory.BackendName} (available: {factory.IsAvailable})");
        Console.WriteLine($"Status:          {factory.DiagnosticStatus}");
        Console.WriteLine();

        if (!factory.IsAvailable)
        {
            Console.WriteLine("GPU I2C backend is not ready; 0 bytes sent on any bus.");
            return 1;
        }

        var buses = factory.OpenBuses();
        Console.WriteLine($"Opened {buses.Count} GPU I2C bus(es) (firewall locked to 0x{GpuI2cAddressFirewall.MinAllowedAddress:X2}..0x{GpuI2cAddressFirewall.MaxAllowedAddress:X2}; EDID/DDC 0x50..0x57 and VRM addresses blocked):");
        try
        {
            foreach (var bus in buses)
            {
                Describe(bus, manifests);
            }
        }
        finally
        {
            foreach (var bus in buses)
            {
                bus.Dispose();
            }
        }

        return 0;
    }

    private static void Describe(IGpuI2cBus bus, IDeviceManifestCatalog? manifests)
    {
        var pci = bus.PciId;
        Console.WriteLine($"  {bus.AdapterName}  [{bus.BusId}]");
        Console.WriteLine($"    location: {bus.Location}");
        Console.WriteLine($"    PCI:      {pci.VendorId:X4}:{pci.DeviceId:X4}  SUBSYS {pci.SubVendorId:X4}:{pci.SubDeviceId:X4}");

        var matches = GpuPciCatalog.FindMatches(pci, manifests);
        if (matches.Count == 0)
        {
            Console.WriteLine("    catalog:  no match");
        }

        foreach (var match in matches)
        {
            Console.WriteLine($"    catalog:  {match.Name} ({match.Protocol}, 0x{match.I2cAddress:X2})");
        }

        var asusAddress = matches.FirstOrDefault(static m => m.Protocol == DeviceProtocolFamily.AsusAuraGpuI2c)?.I2cAddress;
        if (asusAddress is null && pci.SubVendorId == GpuPciIdentity.AsusSubVendorId)
        {
            asusAddress = AsusAuraGpuProtocol.DefaultSlaveAddress;
        }

        if (asusAddress is not { } address)
        {
            Console.WriteLine("    (no read-only identification for this vendor; 0 bytes sent)");
            Console.WriteLine();
            return;
        }

        using var busLock = bus.AcquireBusLock();
        if (!AsusAuraGpuProtocol.TryReadControllerName(bus, address, out var controller))
        {
            Console.WriteLine($"    ASUS Aura 0x{address:X2}: no response (read-only name read failed)");
        }
        else
        {
            var signature = AsusAuraGpuProtocol.IsAuraGpuSignature(controller) ? "Aura GPU signature" : "unknown signature";
            var leds = AsusAuraGpuProtocol.ReadLedCount(bus, address, out var detected);
            Console.WriteLine($"    ASUS Aura 0x{address:X2}: controller '{controller}' ({signature}), {leds} LEDs{(detected ? string.Empty : " (not detected, default)")}");
        }

        Console.WriteLine("    mode/color/save writes sent: 0 (read-only; only register-pointer selects were written)");
        Console.WriteLine();
    }
}
