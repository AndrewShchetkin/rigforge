namespace RigForge.Infrastructure.CrossPlatform.Smbus;

/// <summary>
/// Decides whether a DRAM probe may open the SMBus.
/// When <paramref name="requireSmbiosMatch"/> is true (the default), the result is
/// <paramref name="matchedCount"/> &gt; 0 and nothing else. An unreadable DMI table is not a bypass:
/// module identity has to come from SMBIOS Type 17 or from a cached sysfs SPD EEPROM, udev, or EDAC.
/// <c>RequireSmbiosMatch: false</c> is an explicit opt-in that allows a probe with no matched module.
/// </summary>
internal static class SmbusProbePolicy
{
    public static bool AllowsBusProbe(ISmbusControllerFactory factory, bool requireSmbiosMatch, int matchedCount)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!requireSmbiosMatch)
        {
            return true;
        }

        return matchedCount > 0;
    }
}
