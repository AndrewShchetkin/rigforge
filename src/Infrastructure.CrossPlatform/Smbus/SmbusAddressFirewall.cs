namespace RigForge.Infrastructure.CrossPlatform.Smbus;

/// <summary>
/// Universal bus-level SMBus address firewall enforced by every <see cref="ISmbusController"/> before issuing
/// hardware transactions. Protects system-critical non-RGB SMBus address ranges (SMBus reserved/host-notify,
/// DDR4 SPD page lock, DDR5 PMIC voltage regulators, and DDR4/DDR5 SPD5118 EEPROMs) regardless of protocol.
/// </summary>
public static class SmbusAddressFirewall
{
    public static void ValidateAddress(byte address)
    {
        var forbiddenReason = address switch
        {
            <= 0x0F => "SMBus reserved / host notify / general call range (0x00..0x0F)",
            0x36 or 0x37 => "DDR4 SPD page protection range (0x36..0x37)",
            >= 0x48 and <= 0x4F => "DDR5 PMIC voltage regulator range (0x48..0x4F)",
            >= 0x50 and <= 0x57 => "DDR4/DDR5 SPD5118 EEPROM / hub range (0x50..0x57)",
            >= 0x78 => "SMBus reserved / 10-bit address range (0x78..0x7F)",
            _ => null,
        };

        if (forbiddenReason is not null)
        {
            throw new ArgumentOutOfRangeException(
                nameof(address),
                $"0x{address:X2}",
                $"SMBus address firewall blocked access to 0x{address:X2}: {forbiddenReason}.");
        }
    }
}
