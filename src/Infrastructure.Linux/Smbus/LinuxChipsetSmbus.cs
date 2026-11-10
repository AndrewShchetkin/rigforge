namespace RigForge.Infrastructure.Linux.Smbus;

/// <summary>
/// Chipset SMBus adapter names exposed by <c>/sys/class/i2c-adapter/i2c-*/name</c>.
/// Display DDC buses (NVIDIA i2c, AMDGPU DM) are never accepted.
/// </summary>
internal static class LinuxChipsetSmbus
{
    public static bool IsAdapter(string? adapterName)
    {
        if (string.IsNullOrWhiteSpace(adapterName))
        {
            return false;
        }

        if (Contains(adapterName, "NVIDIA")
            || Contains(adapterName, "AMDGPU")
            || Contains(adapterName, "AMD GPU")
            || Contains(adapterName, "HDMI")
            || Contains(adapterName, "DDC")
            || Contains(adapterName, "dmdc")
            || Contains(adapterName, "DP-HDMI"))
        {
            return false;
        }

        if (Contains(adapterName, "PIIX4")
            || Contains(adapterName, "I801")
            || Contains(adapterName, "KERNCZ")
            || Contains(adapterName, "SMBus adapter")
            || Contains(adapterName, "Synopsys DesignWare")
            || (Contains(adapterName, "NCT6775") && Contains(adapterName, "SMBus")))
        {
            return true;
        }

        return false;
    }

    private static bool Contains(string text, string value) =>
        text.Contains(value, StringComparison.OrdinalIgnoreCase);
}
