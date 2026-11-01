using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Asus;
using RigForge.Infrastructure.CrossPlatform.Nzxt;
using RigForge.Infrastructure.CrossPlatform.Persistence;

namespace RigForge.Infrastructure.CrossPlatform.Manifests;

public sealed class JsonDeviceManifestCatalog : IDeviceManifestCatalog
{
    private static readonly int[] AsusMainboardPids =
    [
        0x1867, 0x1872, 0x18A3, 0x18A5, 0x18F3, 0x1939, 0x19AF, 0x1AA6, 0x1BED,
    ];

    private readonly IReadOnlyList<DeviceManifest> _all;
    private readonly Dictionary<(int VendorId, int ProductId), DeviceManifest> _byUsb;

    [ActivatorUtilitiesConstructor]
    public JsonDeviceManifestCatalog(IOptions<StorageOptions>? options = null)
        : this(Path.Combine(JsonProfileStore.ResolveDirectory(options?.Value.DataDirectory), "devices"))
    {
    }

    public JsonDeviceManifestCatalog(string customDirectory)
    {
        var manifests = BuildBuiltInManifests();

        if (!string.IsNullOrWhiteSpace(customDirectory))
        {
            LoadDirectoryInto(customDirectory, manifests);
            var nestedDevicesDir = Path.Combine(customDirectory, "devices");
            if (Directory.Exists(nestedDevicesDir))
            {
                LoadDirectoryInto(nestedDevicesDir, manifests);
            }
        }

        _all = manifests;
        _byUsb = new Dictionary<(int VendorId, int ProductId), DeviceManifest>(manifests.Count);
        foreach (var manifest in manifests)
        {
            _byUsb[(manifest.VendorId, manifest.ProductId)] = manifest;
        }
    }

    public IReadOnlyList<DeviceManifest> All => _all;

    public IReadOnlyList<DeviceManifest> ForProtocol(DeviceProtocolFamily protocol) =>
        _all.Where(m => m.Protocol == protocol).ToArray();

    public DeviceManifest? FindUsb(int vendorId, int productId) =>
        _byUsb.GetValueOrDefault((vendorId, productId));

    private static List<DeviceManifest> BuildBuiltInManifests()
    {
        var list = new List<DeviceManifest>();

        foreach (var pid in AsusMainboardPids)
        {
            list.Add(new DeviceManifest(
                Id: $"asus-aura-{pid:x4}",
                Name: $"ASUS Aura USB Mainboard (0x{pid:X4})",
                Vendor: "ASUS",
                Protocol: DeviceProtocolFamily.AsusAuraUsb,
                VendorId: AuraUsbProtocol.VendorId,
                ProductId: pid,
                UsagePage: AuraUsbProtocol.UsagePage,
                MinOutputReportLength: AuraUsbProtocol.ReportLength));
        }

        foreach (var kraken in NzxtCatalog.Krakens.Values)
        {
            list.Add(new DeviceManifest(
                Id: $"nzxt-kraken-{kraken.ProductId:x4}",
                Name: kraken.Name,
                Vendor: "NZXT",
                Protocol: DeviceProtocolFamily.NzxtKraken,
                VendorId: NzxtCatalog.VendorId,
                ProductId: kraken.ProductId,
                PumpHeader: kraken.PumpHeader,
                FanHeader: kraken.FanHeader,
                Display: new DeviceDisplayManifest(kraken.LcdWidth, kraken.LcdHeight, kraken.LcdExperimental)));
        }

        foreach (var hue2 in NzxtCatalog.Hue2Devices.Values)
        {
            list.Add(new DeviceManifest(
                Id: $"nzxt-hue2-{hue2.ProductId:x4}",
                Name: hue2.Name,
                Vendor: "NZXT",
                Protocol: DeviceProtocolFamily.NzxtHue2,
                VendorId: NzxtCatalog.VendorId,
                ProductId: hue2.ProductId,
                FanChannels: hue2.FanChannels,
                LedChannels: hue2.LedChannels));
        }

        return list;
    }

    private static void LoadDirectoryInto(string directory, List<DeviceManifest> manifests)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var json = File.ReadAllText(file).Trim();
            if (json.Length == 0)
            {
                continue;
            }

            if (json.StartsWith('['))
            {
                var items = JsonSerializer.Deserialize(json, DeviceManifestJsonContext.Default.DeviceManifestArray);
                if (items is not null)
                {
                    foreach (var item in items)
                    {
                        Upsert(manifests, item);
                    }
                }
            }
            else
            {
                var item = JsonSerializer.Deserialize(json, DeviceManifestJsonContext.Default.DeviceManifest);
                if (item is not null)
                {
                    Upsert(manifests, item);
                }
            }
        }
    }

    private static void Upsert(List<DeviceManifest> manifests, DeviceManifest manifest)
    {
        var index = manifests.FindIndex(m => m.VendorId == manifest.VendorId && m.ProductId == manifest.ProductId);
        if (index >= 0)
        {
            manifests[index] = manifest;
        }
        else
        {
            manifests.Add(manifest);
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(DeviceManifest))]
[JsonSerializable(typeof(DeviceManifest[]))]
public sealed partial class DeviceManifestJsonContext : JsonSerializerContext;
