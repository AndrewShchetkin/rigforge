namespace RigForge.Infrastructure.CrossPlatform.Gpu;

/// <summary>
/// Restricts GPU I2C traffic to the RGB controller window. Blocks EDID/DDC (0x50..0x57) and everything below
/// 0x60 (VRM controllers, thermal sensors, system addresses) plus the reserved 0x78+ range.
/// </summary>
public static class GpuI2cAddressFirewall
{
    public const byte MinAllowedAddress = 0x60;
    public const byte MaxAllowedAddress = 0x77;

    public static bool IsAllowed(byte slaveAddress) =>
        slaveAddress is >= MinAllowedAddress and <= MaxAllowedAddress;

    public static void ValidateSlaveAddress(byte slaveAddress)
    {
        if (IsAllowed(slaveAddress))
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            nameof(slaveAddress),
            slaveAddress,
            $"GPU I2C address 0x{slaveAddress:X2} is blocked. Only 0x{MinAllowedAddress:X2}..0x{MaxAllowedAddress:X2} is allowed; " +
            "0x50..0x57 (EDID/DDC) and addresses below 0x60 (VRM/system) must never be written.");
    }
}
