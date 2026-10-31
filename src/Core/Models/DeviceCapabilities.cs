namespace RigForge.Core.Models;

[Flags]
public enum DeviceCapabilities
{
    None = 0,
    Rgb = 1,
    FanControl = 2,
    Sensors = 4,
    Lcd = 8,
}
