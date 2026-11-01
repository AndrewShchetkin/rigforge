using RigForge.Core.Models;

namespace RigForge.Core.Interfaces;

public interface IDeviceManifestCatalog
{
    IReadOnlyList<DeviceManifest> All { get; }

    IReadOnlyList<DeviceManifest> ForProtocol(DeviceProtocolFamily protocol);

    DeviceManifest? FindUsb(int vendorId, int productId);
}
