using RigForge.Core.Models;

namespace RigForge.Infrastructure.CrossPlatform.Nzxt;

/// <param name="PumpHeader">Bytes after 0x72 that address the pump profile.</param>
/// <param name="FanHeader">Bytes after 0x72 that address the radiator fan profile.</param>
/// <param name="LcdExperimental">liquidctl reports screen uploads as unreliable on this model.</param>
internal sealed record KrakenModel(
    int ProductId,
    string Name,
    int LcdWidth,
    int LcdHeight,
    byte[] PumpHeader,
    byte[] FanHeader,
    bool LcdExperimental = false,
    int RgbRingLeds = 0);

internal sealed record KrakenX3Model(int ProductId, string Name);

internal sealed record Hue2Model(int ProductId, string Name, int FanChannels, int LedChannels);

internal sealed record Hue2Accessory(string Name, int LedCount, LedTopology Topology);

/// <summary>Supported NZXT products (VID 0x1E71), compiled from liquidctl and OpenRGB.</summary>
internal static class NzxtCatalog
{
    public const int VendorId = 0x1E71;

    private static readonly byte[] KrakenZ3Pump = [0x01, 0x00, 0x00];
    private static readonly byte[] KrakenZ3Fan = [0x02, 0x00, 0x00];
    private static readonly byte[] Kraken2023Pump = [0x01, 0x01, 0x00];
    private static readonly byte[] Kraken2023Fan = [0x02, 0x01, 0x01];

    public static readonly IReadOnlyDictionary<int, KrakenModel> Krakens = new Dictionary<int, KrakenModel>
    {
        [0x3008] = new(0x3008, "NZXT Kraken Z (Z53/Z63/Z73)", 320, 320, KrakenZ3Pump, KrakenZ3Fan),
        [0x300C] = new(0x300C, "NZXT Kraken 2023 Elite", 640, 640, Kraken2023Pump, Kraken2023Fan, LcdExperimental: true),
        [0x300E] = new(0x300E, "NZXT Kraken 2023", 240, 240, Kraken2023Pump, Kraken2023Fan),
        [0x3012] = new(0x3012, "NZXT Kraken 2024 Elite RGB", 640, 640, Kraken2023Pump, Kraken2023Fan, LcdExperimental: true, RgbRingLeds: NzxtProtocol.KrakenEliteRingLeds),
        [0x3014] = new(0x3014, "NZXT Kraken 2024 Plus", 240, 240, Kraken2023Pump, Kraken2023Fan),
    };

    /// <summary>Kraken X3 coolers (X53/X63/X73). Ring and logo RGB, pump control, no LCD.</summary>
    public static readonly IReadOnlyDictionary<int, KrakenX3Model> KrakenX3Devices = new Dictionary<int, KrakenX3Model>
    {
        [0x2007] = new(0x2007, "NZXT Kraken X3 (X53/X63/X73)"),
        [0x2014] = new(0x2014, "NZXT Kraken X3 (X53/X63/X73)"),
    };

    public static readonly IReadOnlyDictionary<int, Hue2Model> Hue2Devices = new Dictionary<int, Hue2Model>
    {
        [0x2001] = new(0x2001, "NZXT HUE 2", 0, 4),
        [0x2002] = new(0x2002, "NZXT HUE 2 Ambient", 0, 2),
        [0x2005] = new(0x2005, "NZXT Motherboard RGB/Fan Controller", 3, 2),
        [0x200B] = new(0x200B, "NZXT Motherboard RGB/Fan Controller", 3, 2),
        [0x2006] = new(0x2006, "NZXT Smart Device V2", 3, 2),
        [0x200D] = new(0x200D, "NZXT Smart Device V2", 3, 2),
        [0x200F] = new(0x200F, "NZXT Smart Device V2", 3, 2),
        [0x2009] = new(0x2009, "NZXT RGB & Fan Controller", 3, 2),
        [0x200E] = new(0x200E, "NZXT RGB & Fan Controller", 3, 2),
        [0x2010] = new(0x2010, "NZXT RGB & Fan Controller", 3, 2),
        [0x2011] = new(0x2011, "NZXT RGB & Fan Controller (3+6)", 3, 6),
        [0x2019] = new(0x2019, "NZXT RGB & Fan Controller (3+6)", 3, 6),
        [0x201B] = new(0x201B, "NZXT B650E Motherboard Controller", 3, 6),
        [0x201F] = new(0x201F, "NZXT RGB & Fan Controller (3+6)", 3, 6),
        [0x2020] = new(0x2020, "NZXT RGB & Fan Controller (3+6)", 3, 6),
        [0x2022] = new(0x2022, "NZXT Control Hub", 5, 6),
        [0x2012] = new(0x2012, "NZXT RGB Controller", 0, 3),
        [0x2021] = new(0x2021, "NZXT RGB Controller", 0, 3),
    };

    /// <summary>LED accessories reported by HUE 2 controllers, with LED counts from OpenRGB.</summary>
    public static readonly IReadOnlyDictionary<byte, Hue2Accessory> Accessories = new Dictionary<byte, Hue2Accessory>
    {
        [0x01] = new("HUE+ LED Strip", 10, LedTopology.Linear),
        [0x02] = new("AER RGB 1", 8, LedTopology.Ring),
        [0x04] = new("HUE 2 LED Strip 300 mm", 10, LedTopology.Linear),
        [0x05] = new("HUE 2 LED Strip 250 mm", 8, LedTopology.Linear),
        [0x06] = new("HUE 2 LED Strip 200 mm", 6, LedTopology.Linear),
        [0x08] = new("HUE 2 Cable Comb", 14, LedTopology.Linear),
        [0x09] = new("HUE 2 Underglow 300 mm", 15, LedTopology.Linear),
        [0x0A] = new("HUE 2 Underglow 200 mm", 10, LedTopology.Linear),
        [0x0B] = new("AER RGB 2 120 mm", 8, LedTopology.Ring),
        [0x0C] = new("AER RGB 2 140 mm", 8, LedTopology.Ring),
        [0x10] = new("Kraken X3 Pump Ring", 8, LedTopology.Ring),
        [0x11] = new("Kraken X3 Pump Logo", 1, LedTopology.Point),
        [0x13] = new("F120 RGB", 18, LedTopology.Ring),
        [0x14] = new("F140 RGB", 18, LedTopology.Ring),
        [0x15] = new("F120 RGB Duo", 20, LedTopology.Ring),
        [0x16] = new("F140 RGB Duo", 20, LedTopology.Ring),
        [0x17] = new("F120 RGB Core", 8, LedTopology.Ring),
        [0x18] = new("F140 RGB Core", 8, LedTopology.Ring),
        [0x19] = new("F120 RGB Core (case)", 8, LedTopology.Ring),
        [0x1C] = new("F280 RGB Core (case)", 16, LedTopology.Composite),
        [0x1D] = new("F360 RGB Core (case)", 24, LedTopology.Composite),
        [0x1E] = new("Kraken Elite Ring", 24, LedTopology.Ring),
        [0x1F] = new("F420 RGB", 24, LedTopology.Composite),
    };

    /// <summary>Expands an accessory into ring/strip segments for the default spatial layout.</summary>
    public static IEnumerable<(LedTopology Topology, int LedCount)> Segments(Hue2Accessory accessory) =>
        accessory.Topology == LedTopology.Composite
            ? Enumerable.Repeat((LedTopology.Ring, 8), accessory.LedCount / 8)
            : [(accessory.Topology, accessory.LedCount)];
}
