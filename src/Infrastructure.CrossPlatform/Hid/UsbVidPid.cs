using HidSharp;

namespace RigForge.Infrastructure.CrossPlatform.Hid;

internal static class UsbVidPid
{
    public const string PropertyName = "vidPid";

    public static string Format(int vendorId, int productId) => $"{vendorId:X4}:{productId:X4}";

    public static string Format(HidDevice? device, int vendorId, int productId) =>
        device is null ? Format(vendorId, productId) : Format(device.VendorID, device.ProductID);
}
