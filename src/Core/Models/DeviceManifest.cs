namespace RigForge.Core.Models;

public enum DeviceProtocolFamily
{
    AsusAuraUsb,
    NzxtKraken,
    NzxtHue2,
    KingstonFuryDdr5,
}

public sealed record DeviceDisplayManifest(
    int Width,
    int Height,
    bool Experimental = false);

public sealed record DeviceManifest(
    string Id,
    string Name,
    string Vendor,
    DeviceProtocolFamily Protocol,
    int VendorId,
    int ProductId,
    int? UsagePage = null,
    int MinOutputReportLength = 0,
    int FanChannels = 0,
    int LedChannels = 0,
    byte[]? PumpHeader = null,
    byte[]? FanHeader = null,
    DeviceDisplayManifest? Display = null);
