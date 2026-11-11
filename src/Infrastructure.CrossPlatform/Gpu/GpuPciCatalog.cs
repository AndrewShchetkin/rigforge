using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.Gpu;

public sealed record GpuCatalogEntry(
    GpuPciIdentity PciId,
    string Name,
    string Vendor,
    DeviceProtocolFamily Protocol,
    byte I2cAddress,
    string? Variant = null);

/// <summary>Built-in NVIDIA RTX 30/40/50 board-partner cards with a known I2C RGB controller (ASUS, MSI, Gigabyte).</summary>
public static class GpuPciCatalog
{
    private const byte AsusAddress = 0x67;
    private const byte MsiAddress = 0x68;
    private const byte GigabyteGen4Address = 0x75;
    private const byte GigabyteGen2Address = 0x71;

    private static readonly DeviceProtocolFamily[] GpuProtocols =
    [
        DeviceProtocolFamily.AsusAuraGpuI2c,
        DeviceProtocolFamily.MsiMysticGpuI2c,
        DeviceProtocolFamily.GigabyteFusionGpuI2c,
    ];

    private static readonly GpuCatalogEntry[] Entries = BuildEntries();
    private static readonly IReadOnlyList<GpuCatalogEntry> ReadOnlyEntries = Array.AsReadOnly(Entries);

    public static IReadOnlyList<GpuCatalogEntry> All => ReadOnlyEntries;

    /// <summary>
    /// All candidate controllers for a card, best candidate first. Valid custom manifests (JSON overrides) replace
    /// the built-in entries for that exact PCI identity. Manifests with a firewall-blocked address are ignored.
    /// </summary>
    public static IReadOnlyList<GpuCatalogEntry> FindMatches(GpuPciIdentity pciId, IDeviceManifestCatalog? manifests = null)
    {
        if (manifests is not null)
        {
            var custom = new List<GpuCatalogEntry>();
            foreach (var manifest in manifests.All)
            {
                if (TryCreateEntry(manifest, pciId, out var entry))
                {
                    custom.Add(entry);
                }
            }

            if (custom.Count > 0)
            {
                return custom;
            }
        }

        return Entries.Where(e => e.PciId == pciId).ToArray();
    }

    public static GpuCatalogEntry? FindMatch(GpuPciIdentity pciId, IDeviceManifestCatalog? manifests = null)
    {
        var matches = FindMatches(pciId, manifests);
        return matches.Count > 0 ? matches[0] : null;
    }

    private static bool TryCreateEntry(DeviceManifest manifest, GpuPciIdentity pciId, out GpuCatalogEntry entry)
    {
        entry = null!;
        if (Array.IndexOf(GpuProtocols, manifest.Protocol) < 0
            || manifest.VendorId != pciId.VendorId
            || manifest.ProductId != pciId.DeviceId
            || manifest.SubVendorId != pciId.SubVendorId
            || manifest.SubDeviceId != pciId.SubDeviceId
            || manifest.I2cAddress is not { } address
            || !GpuI2cAddressFirewall.IsAllowed(address))
        {
            return false;
        }

        entry = new GpuCatalogEntry(pciId, manifest.Name, manifest.Vendor, manifest.Protocol, address, manifest.Variant);
        return true;
    }

    private static GpuCatalogEntry[] BuildEntries()
    {
        var list = new List<GpuCatalogEntry>(256);
        AddAsus(list);
        AddMsi(list);
        AddGigabyte(list);
        return [.. list];
    }

    private static void Add(
        List<GpuCatalogEntry> list,
        ushort subVendorId,
        ushort deviceId,
        ushort subDeviceId,
        string name,
        string vendor,
        DeviceProtocolFamily protocol,
        byte address,
        string? variant = null) =>
        list.Add(new GpuCatalogEntry(
            new GpuPciIdentity(GpuPciIdentity.NvidiaVendorId, deviceId, subVendorId, subDeviceId),
            name,
            vendor,
            protocol,
            address,
            variant));

    private static void AddAsus(List<GpuCatalogEntry> list)
    {
        void Asus(ushort deviceId, string name, params ushort[] subDeviceIds)
        {
            foreach (var sub in subDeviceIds)
            {
                Add(list, GpuPciIdentity.AsusSubVendorId, deviceId, sub, name, "ASUS", DeviceProtocolFamily.AsusAuraGpuI2c, AsusAddress);
            }
        }

        // Blackwell (RTX 50)
        Asus(0x2D04, "ASUS GeForce RTX 5060 Ti TUF Gaming", 0x89F0, 0x89F1, 0x89F2, 0x89F3, 0x8A08);
        Asus(0x2F04, "ASUS GeForce RTX 5070 TUF GAMING", 0x89F2);
        Asus(0x2F04, "ASUS GeForce RTX 5070 ROG Strix OC", 0x8A18);
        Asus(0x2C05, "ASUS GeForce RTX 5070 Ti TUF GAMING", 0x89F4, 0x8A0C);
        Asus(0x2C05, "ASUS GeForce RTX 5070 Ti ROG Strix OC", 0x8A0D);
        Asus(0x2C05, "ASUS GeForce RTX 5070 Ti BTF OC", 0x8A37);
        Asus(0x2C05, "ASUS GeForce RTX 5070 Ti ProArt", 0x8A53);
        Asus(0x2C02, "ASUS GeForce RTX 5080 TUF Gaming / ROG Astral", 0x89E8, 0x89E9, 0x89EA, 0x89EB, 0x89EC, 0x8A0A, 0x8A0B, 0x8A20);
        Asus(0x2B85, "ASUS GeForce RTX 5090 TUF Gaming / ROG Astral / ROG Matrix", 0x89E0, 0x89E1, 0x89E2, 0x89E3, 0x89E4, 0x8A00, 0x8A01, 0x8A10);

        // Ada Lovelace (RTX 40)
        Asus(0x2684, "ASUS GeForce RTX 4090 ROG Strix / TUF Gaming", 0x889C, 0x889D, 0x88A1, 0x88A2);
        Asus(0x2704, "ASUS GeForce RTX 4080 ROG Strix / TUF Gaming", 0x889F, 0x88A3);
        Asus(0x2702, "ASUS GeForce RTX 4080 SUPER ROG Strix / TUF Gaming", 0x8973, 0x8975);
        Asus(0x2705, "ASUS GeForce RTX 4070 Ti SUPER ROG Strix / TUF Gaming", 0x8977, 0x8979);
        Asus(0x2782, "ASUS GeForce RTX 4070 Ti ROG Strix / TUF Gaming", 0x88AC, 0x88AE);
        Asus(0x2786, "ASUS GeForce RTX 4070 ROG Strix / TUF Gaming", 0x88DF, 0x88E1);

        // Ampere (RTX 30)
        Asus(0x2204, "ASUS GeForce RTX 3090 ROG Strix / TUF Gaming", 0x87AF, 0x87B2);
        Asus(0x2206, "ASUS GeForce RTX 3080 ROG Strix / TUF Gaming", 0x87B0, 0x87B3);
        Asus(0x2484, "ASUS GeForce RTX 3070 ROG Strix / TUF Gaming", 0x87BE, 0x87C0);
    }

    private static void AddMsi(List<GpuCatalogEntry> list)
    {
        void Msi(ushort deviceId, string model, string variant, params ushort[] subDeviceIds)
        {
            var tier = variant[(variant.IndexOf(':', StringComparison.Ordinal) + 1)..];
            foreach (var sub in subDeviceIds)
            {
                Add(list, GpuPciIdentity.MsiSubVendorId, deviceId, sub, $"MSI GeForce RTX {model} {tier}", "MSI", DeviceProtocolFamily.MsiMysticGpuI2c, MsiAddress, variant);
            }
        }

        const string trio = "Blackwell:GAMING TRIO";
        const string vanguard = "Blackwell:VANGUARD";
        const string suprim = "Blackwell:SUPRIM SOC";

        // Blackwell (RTX 50)
        Msi(0x2D05, "5060", trio, 0x5372);
        Msi(0x2D04, "5060 Ti", trio, 0x5352, 0x5353, 0x5360, 0x5361);
        Msi(0x2D04, "5060 Ti", vanguard, 0x5354);
        Msi(0x2F04, "5070", trio, 0x5321);
        Msi(0x2F04, "5070", vanguard, 0x5322);
        Msi(0x2C05, "5070 Ti", vanguard, 0x5314, 0x5318);
        Msi(0x2C05, "5070 Ti", trio, 0x5315);
        Msi(0x2C02, "5080", suprim, 0x5313);
        Msi(0x2C02, "5080", vanguard, 0x5314);
        Msi(0x2C02, "5080", trio, 0x5315);
        Msi(0x2B85, "5090", suprim, 0x5301);
        Msi(0x2B85, "5090", vanguard, 0x5302);
        Msi(0x2B85, "5090", trio, 0x5303);

        const string lovelaceTrio = "Lovelace:GAMING TRIO";
        const string lovelaceSuprim = "Lovelace:SUPRIM";

        // Ada Lovelace (RTX 40)
        Msi(0x2803, "4060 Ti", lovelaceTrio, 0x5151, 0x5152);
        Msi(0x2786, "4070", lovelaceTrio, 0x5136);
        Msi(0x2782, "4070 Ti", lovelaceTrio, 0x5132, 0x5139);
        Msi(0x2782, "4070 Ti", lovelaceSuprim, 0x5133);
        Msi(0x2705, "4070 Ti SUPER", lovelaceTrio, 0x5139);
        Msi(0x2704, "4080", lovelaceSuprim, 0x5110);
        Msi(0x2704, "4080", lovelaceTrio, 0x5111, 0x5115);
        Msi(0x2702, "4080 SUPER", lovelaceSuprim, 0x5110);
        Msi(0x2702, "4080 SUPER", lovelaceTrio, 0x5111);
        Msi(0x2684, "4090", lovelaceSuprim, 0x5102, 0x5104, 0x5105, 0x5106);
        Msi(0x2684, "4090", lovelaceTrio, 0x5103);
    }

    private static void AddGigabyte(List<GpuCatalogEntry> list)
    {
        void Gigabyte(ushort deviceId, string model, string variant, byte address, params ushort[] subDeviceIds)
        {
            var tier = variant[(variant.IndexOf(':', StringComparison.Ordinal) + 1)..];
            foreach (var sub in subDeviceIds)
            {
                Add(list, GpuPciIdentity.GigabyteSubVendorId, deviceId, sub, $"GIGABYTE GeForce RTX {model} {tier}", "Gigabyte", DeviceProtocolFamily.GigabyteFusionGpuI2c, address, variant);
            }
        }

        const byte gen4 = GigabyteGen4Address;
        const string gaming = "Gen4:GAMING";
        const string eagle = "Gen4:EAGLE";
        const string aero = "Gen4:AERO";
        const string master = "Gen4:AORUS MASTER";
        const string waterforce = "Gen4:WATERFORCE";
        const string waterforceXtreme = "Gen4:WATERFORCE XTREME";

        // Gen4 (0x75) primary candidates
        Gigabyte(0x2D05, "5060", aero, gen4, 0x41A0);
        Gigabyte(0x2D04, "5060 Ti", master, gen4, 0x4190);
        Gigabyte(0x2D04, "5060 Ti", gaming, gen4, 0x4191, 0x4193);
        Gigabyte(0x2D04, "5060 Ti", aero, gen4, 0x4192);
        Gigabyte(0x2F04, "5070", gaming, gen4, 0x4174);
        Gigabyte(0x2F04, "5070", master, gen4, 0x4175);
        Gigabyte(0x2F04, "5070", eagle, gen4, 0x417D, 0x4185);
        Gigabyte(0x2F04, "5070", aero, gen4, 0x4184);
        Gigabyte(0x2C05, "5070 Ti", master, gen4, 0x417B);
        Gigabyte(0x2C05, "5070 Ti", aero, gen4, 0x417F);
        Gigabyte(0x2C05, "5070 Ti", eagle, gen4, 0x4180, 0x4182);
        Gigabyte(0x2C05, "5070 Ti", gaming, gen4, 0x4181);
        Gigabyte(0x2C02, "5080", gaming, gen4, 0x4176);
        Gigabyte(0x2C02, "5080", master, gen4, 0x4178, 0x418C);
        Gigabyte(0x2C02, "5080", aero, gen4, 0x4179);
        Gigabyte(0x2C02, "5080", waterforceXtreme, gen4, 0x418A);
        Gigabyte(0x2B85, "5090", master, gen4, 0x416E, 0x4199);
        Gigabyte(0x2B85, "5090", gaming, gen4, 0x416F);
        Gigabyte(0x2B85, "5090", waterforce, gen4, 0x4171, 0x4172);

        // Gen2 (0x71) secondary candidates: some AORUS Master boards answer on the older controller instead.
        const byte gen2 = GigabyteGen2Address;
        Gigabyte(0x2F04, "5070", "Gen2:GAMING_GEN2", gen2, 0x4175);
        Gigabyte(0x2C02, "5080", "Gen2:SINGLE_GEN2", gen2, 0x4178, 0x418C);
    }
}
