using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;
using RigForge.Infrastructure.CrossPlatform.Asus;
using RigForge.Infrastructure.CrossPlatform.AsRock;
using RigForge.Infrastructure.CrossPlatform.CoolerMaster;
using RigForge.Infrastructure.CrossPlatform.Corsair;
using RigForge.Infrastructure.CrossPlatform.Gigabyte;
using RigForge.Infrastructure.CrossPlatform.LianLi;
using RigForge.Infrastructure.CrossPlatform.Msi;
using RigForge.Infrastructure.CrossPlatform.Nzxt;
using RigForge.Infrastructure.CrossPlatform.Peripherals.Logitech;
using RigForge.Infrastructure.CrossPlatform.Peripherals.Razer;
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

        foreach (var krakenX3 in NzxtCatalog.KrakenX3Devices.Values)
        {
            list.Add(Usb(
                $"nzxt-kraken-x3-{krakenX3.ProductId:x4}",
                krakenX3.Name,
                "NZXT",
                DeviceProtocolFamily.NzxtKrakenX3,
                NzxtCatalog.VendorId,
                krakenX3.ProductId));
        }

        AddCorsair(list);
        AddMsi(list);
        AddGigabyte(list);
        list.Add(Usb(
            "asrock-polychrome-01a2",
            "ASRock Polychrome USB",
            "ASRock",
            DeviceProtocolFamily.AsRockPolychromeUsb,
            AsRockPolychromeProtocol.VendorId,
            AsRockPolychromeProtocol.ProductId,
            minOutput: AsRockPolychromeProtocol.ReportLength));
        AddLianLi(list);
        AddCoolerMaster(list);
        AddLogitech(list);
        AddRazer(list);

        return list;
    }

    private static void AddCorsair(List<DeviceManifest> list)
    {
        (int Pid, string Name, DeviceProtocolFamily Protocol, int Report)[] models =
        [
            (CorsairProtocol.LightingNodeProProductId, "Corsair Lighting Node Pro", DeviceProtocolFamily.CorsairLightingNode, CorsairProtocol.LightingNodeReportLength),
            (CorsairProtocol.CommanderProProductId, "Corsair Commander Pro", DeviceProtocolFamily.CorsairLightingNode, CorsairProtocol.LightingNodeReportLength),
            (CorsairProtocol.LightingNodeCoreProductId, "Corsair Lighting Node Core", DeviceProtocolFamily.CorsairLightingNode, CorsairProtocol.LightingNodeReportLength),
            (CorsairProtocol.Ls100ProductId, "Corsair LS100", DeviceProtocolFamily.CorsairLightingNode, CorsairProtocol.LightingNodeReportLength),
            (CorsairProtocol.CommanderCoreProductId, "Corsair Commander Core", DeviceProtocolFamily.CorsairCommanderCore, CorsairProtocol.CommanderCoreMinReportLength),
            (CorsairProtocol.CommanderCoreXtProductId, "Corsair Commander Core XT", DeviceProtocolFamily.CorsairCommanderCore, CorsairProtocol.CommanderCoreMinReportLength),
            (CorsairProtocol.IcueCommanderCoreProductId, "Corsair iCUE Commander Core", DeviceProtocolFamily.CorsairCommanderCore, CorsairProtocol.CommanderCoreMinReportLength),
        ];

        foreach (var (pid, name, protocol, report) in models)
        {
            list.Add(Usb($"corsair-{pid:x4}", name, "Corsair", protocol, CorsairProtocol.VendorId, pid, minOutput: report));
        }
    }

    private static void AddMsi(List<DeviceManifest> list)
    {
        foreach (var vendorId in MsiMysticLightProtocol.VendorIds)
        {
            foreach (var productId in MsiMysticLightProtocol.ProductIds)
            {
                list.Add(Usb(
                    $"msi-mystic-{vendorId:x4}-{productId:x4}",
                    $"MSI Mystic Light {productId:X4}",
                    "MSI",
                    DeviceProtocolFamily.MsiMysticLightUsb,
                    vendorId,
                    productId));
            }
        }
    }

    private static void AddGigabyte(List<DeviceManifest> list)
    {
        (int Pid, string Name)[] models =
        [
            (GigabyteFusion2Protocol.ProductIdIt8297, "Gigabyte RGB Fusion 2.0 (IT8297)"),
            (GigabyteFusion2Protocol.ProductId5702, "Gigabyte RGB Fusion 2.0 (0x5702)"),
            (GigabyteFusion2Protocol.ProductId5711, "Gigabyte RGB Fusion 2.0 (0x5711)"),
        ];

        foreach (var (pid, name) in models)
        {
            list.Add(Usb(
                $"gigabyte-fusion-{pid:x4}",
                name,
                "Gigabyte",
                DeviceProtocolFamily.GigabyteFusion2Usb,
                GigabyteFusion2Protocol.VendorId,
                pid,
                minOutput: GigabyteFusion2Protocol.ReportLength));
        }
    }

    private static void AddLianLi(List<DeviceManifest> list)
    {
        (int Pid, string Name)[] models =
        [
            (LianLiUniHubProtocol.UniHubSl, "Lian Li UNI HUB SL"),
            (LianLiUniHubProtocol.UniHubAl, "Lian Li UNI HUB AL"),
            (LianLiUniHubProtocol.UniHubSlInfinity, "Lian Li UNI HUB SL-Infinity"),
            (LianLiUniHubProtocol.UniHubSlV2, "Lian Li UNI HUB SL V2"),
            (LianLiUniHubProtocol.UniHubAlV2, "Lian Li UNI HUB AL V2"),
            (LianLiUniHubProtocol.StrimerPlusV2, "Lian Li Strimer Plus V2"),
        ];

        foreach (var (pid, name) in models)
        {
            list.Add(Usb(
                $"lianli-{pid:x4}",
                name,
                "Lian Li",
                DeviceProtocolFamily.LianLiUniHub,
                LianLiUniHubProtocol.VendorId,
                pid,
                minOutput: LianLiUniHubProtocol.ReportLength));
        }
    }

    private static void AddCoolerMaster(List<DeviceManifest> list)
    {
        list.Add(Usb(
            "coolermaster-argb-1011",
            "Cooler Master ARGB Small Controller",
            "Cooler Master",
            DeviceProtocolFamily.CoolerMasterArgb,
            CoolerMasterArgbProtocol.VendorId,
            CoolerMasterArgbProtocol.SmallControllerProductId,
            minOutput: CoolerMasterArgbProtocol.ReportLength));
        list.Add(Usb(
            "coolermaster-argb-0173",
            "Cooler Master ARGB Gen2 Controller",
            "Cooler Master",
            DeviceProtocolFamily.CoolerMasterArgb,
            CoolerMasterArgbProtocol.VendorId,
            CoolerMasterArgbProtocol.Gen2ControllerProductId,
            minOutput: CoolerMasterArgbProtocol.ReportLength));
    }

    private static void AddLogitech(List<DeviceManifest> list)
    {
        var names = new Dictionary<int, string>
        {
            [0xC08B] = "Logitech G502 HERO",
            [0xC07D] = "Logitech G502 Proteus Spectrum",
            [0xC084] = "Logitech G203 / G102",
            [0xC092] = "Logitech G203 Lightsync",
            [0xC088] = "Logitech G Pro Wireless",
            [0xC094] = "Logitech G Pro X Superlight",
            [0xC08C] = "Logitech G502 Lightspeed",
            [0xC336] = "Logitech G213",
            [0xC339] = "Logitech G Pro Keyboard",
            [0xC33F] = "Logitech G815",
            [0xC342] = "Logitech G915",
            [0x0AAA] = "Logitech G733",
        };

        foreach (var productId in LogitechHidPpProtocol.ProductIds.Order())
        {
            var name = names.TryGetValue(productId, out var known) ? known : $"Logitech Lightsync {productId:X4}";
            list.Add(Usb(
                $"logitech-{productId:x4}",
                name,
                "Logitech",
                DeviceProtocolFamily.LogitechHidPp,
                LogitechHidPpProtocol.VendorId,
                productId,
                usagePage: LogitechHidPpProtocol.UsagePage,
                minOutput: LogitechHidPpProtocol.ReportLength));
        }
    }

    private static void AddRazer(List<DeviceManifest> list)
    {
        var names = new Dictionary<int, string>
        {
            [RazerChromaProtocol.ArgbControllerProductId] = "Razer Chroma Addressable RGB Controller",
            [0x0084] = "Razer DeathAdder V2",
            [0x0078] = "Razer Viper Ultimate",
            [0x008A] = "Razer Basilisk V3",
            [0x024E] = "Razer BlackWidow V3",
            [0x0258] = "Razer Huntsman Mini",
            [0x0C00] = "Razer Firefly",
        };

        foreach (var productId in RazerChromaProtocol.ProductIds.Order())
        {
            var name = names.TryGetValue(productId, out var known) ? known : $"Razer Chroma {productId:X4}";
            list.Add(Usb(
                $"razer-{productId:x4}",
                name,
                "Razer",
                DeviceProtocolFamily.RazerChromaHid,
                RazerChromaProtocol.VendorId,
                productId,
                minOutput: RazerChromaProtocol.ReportLength));
        }
    }

    private static DeviceManifest Usb(
        string id,
        string name,
        string vendor,
        DeviceProtocolFamily protocol,
        int vendorId,
        int productId,
        int? usagePage = null,
        int minOutput = 0) =>
        new(
            Id: id,
            Name: name,
            Vendor: vendor,
            Protocol: protocol,
            VendorId: vendorId,
            ProductId: productId,
            UsagePage: usagePage,
            MinOutputReportLength: minOutput);

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
