using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;
using RigForge.Infrastructure.CrossPlatform.Kingston;
using RigForge.Infrastructure.CrossPlatform.Smbus;

namespace RigForge.Infrastructure.Linux.Smbus;

/// <summary>
/// Linux <c>/dev/i2c-*</c> SMBus backend restricted to AMD PIIX4 and Intel i801 chipset adapters
/// and guarded by <see cref="SmbusSafetyGuard"/>.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxI2cSmbusControllerFactory : ISmbusControllerFactory
{
    private const string SysfsI2cAdapterRoot = "/sys/class/i2c-adapter";
    private const string DmiTablePath = "/sys/firmware/dmi/tables/DMI";

    public string BackendName => "i2c-dev";

    public bool IsAvailable => Directory.Exists(SysfsI2cAdapterRoot) && FindChipsetAdapters().Count > 0;

    public string DiagnosticStatus =>
        IsAvailable
            ? "Linux i2c-dev chipset adapters available."
            : "No writable /dev/i2c-* chipset adapter found (load i2c-dev and i2c-piix4/i2c-i801).";

    public IReadOnlyList<SmbiosMemoryStick> GetInstalledMemory()
    {
        try
        {
            if (!File.Exists(DmiTablePath))
            {
                return [];
            }

            var raw = File.ReadAllBytes(DmiTablePath);
            return ParseSmbiosType17(raw);
        }
        catch
        {
            return [];
        }
    }

    public IReadOnlyList<ISmbusController> OpenControllers()
    {
        var result = new List<ISmbusController>();
        foreach (var (busId, adapterName) in FindChipsetAdapters())
        {
            var devPath = $"/dev/{busId}";
            if (LinuxI2cSmbusController.TryOpen(busId, devPath, adapterName) is { } controller)
            {
                result.Add(controller);
            }
        }

        return result;
    }

    internal static IReadOnlyList<SmbiosMemoryStick> ParseSmbiosType17(ReadOnlySpan<byte> dmi)
    {
        var sticks = new List<SmbiosMemoryStick>();
        var offset = 0;

        while (offset + 4 <= dmi.Length)
        {
            var type = dmi[offset];
            var formattedLength = dmi[offset + 1];
            if (formattedLength < 4 || offset + formattedLength > dmi.Length)
            {
                break;
            }

            var stringsStart = offset + formattedLength;
            var strings = ReadStructureStrings(dmi[stringsStart..], out var stringsBytes);
            var nextOffset = stringsStart + stringsBytes;

            if (type == 17 && formattedLength >= 0x1C)
            {
                var sizeRaw = (ushort)(dmi[offset + 0x0C] | (dmi[offset + 0x0D] << 8));
                if (sizeRaw is > 0 and not 0xFFFF)
                {
                    var sizeMb = (sizeRaw & 0x8000) != 0 ? (sizeRaw & 0x7FFF) / 1024 : sizeRaw;
                    var speed = (ushort)(dmi[offset + 0x15] | (dmi[offset + 0x16] << 8));
                    var deviceLocator = GetDmiString(strings, dmi[offset + 0x10]);
                    var bankLocator = GetDmiString(strings, dmi[offset + 0x11]);
                    var manufacturer = GetDmiString(strings, dmi[offset + 0x17]);
                    var serial = GetDmiString(strings, dmi[offset + 0x18]);
                    var partNumber = GetDmiString(strings, dmi[offset + 0x1A]).Trim();

                    if (partNumber.Length > 0)
                    {
                        sticks.Add(new SmbiosMemoryStick(
                            bankLocator,
                            deviceLocator,
                            manufacturer,
                            partNumber,
                            serial,
                            sizeMb,
                            speed));
                    }
                }
            }
            else if (type == 127)
            {
                break;
            }

            offset = nextOffset;
        }

        return sticks;
    }

    private static List<(string BusId, string Name)> FindChipsetAdapters()
    {
        var adapters = new List<(string, string)>();
        if (!Directory.Exists(SysfsI2cAdapterRoot))
        {
            return adapters;
        }

        foreach (var dir in Directory.EnumerateDirectories(SysfsI2cAdapterRoot, "i2c-*"))
        {
            var busId = Path.GetFileName(dir);
            var nameFile = Path.Combine(dir, "name");
            var devPath = $"/dev/{busId}";
            if (!File.Exists(nameFile) || !File.Exists(devPath))
            {
                continue;
            }

            string adapterName;
            try
            {
                adapterName = File.ReadAllText(nameFile).Trim();
            }
            catch
            {
                continue;
            }

            if (adapterName.Contains("PIIX4", StringComparison.OrdinalIgnoreCase)
                || adapterName.Contains("I801", StringComparison.OrdinalIgnoreCase))
            {
                adapters.Add((busId, adapterName));
            }
        }

        return adapters;
    }

    private static List<string> ReadStructureStrings(ReadOnlySpan<byte> tail, out int consumedBytes)
    {
        var result = new List<string>();
        var idx = 0;

        if (tail.Length >= 2 && tail[0] == 0 && tail[1] == 0)
        {
            consumedBytes = 2;
            return result;
        }

        while (idx < tail.Length)
        {
            var end = tail[idx..].IndexOf((byte)0);
            if (end < 0)
            {
                consumedBytes = tail.Length;
                return result;
            }

            result.Add(Encoding.ASCII.GetString(tail.Slice(idx, end)));
            idx += end + 1;

            if (idx < tail.Length && tail[idx] == 0)
            {
                idx++;
                break;
            }
        }

        consumedBytes = idx;
        return result;
    }

    private static string GetDmiString(List<string> strings, byte oneBasedIndex) =>
        oneBasedIndex > 0 && oneBasedIndex <= strings.Count ? strings[oneBasedIndex - 1] : string.Empty;
}

[SupportedOSPlatform("linux")]
internal sealed partial class LinuxI2cSmbusController : ISmbusController
{
    private const ulong I2cSlave = 0x0703;
    private const ulong I2cSmbus = 0x0720;
    private const byte I2cSmbusWrite = 0;
    private const byte I2cSmbusRead = 1;
    private const uint I2cSmbusByteData = 2;
    private const uint I2cSmbusWordData = 3;

    private readonly SafeFileHandle _handle;
    private readonly Lock _gate = new();
    private bool _disposed;

    private LinuxI2cSmbusController(SafeFileHandle handle, string busName, string location)
    {
        _handle = handle;
        BusName = busName;
        Location = location;
    }

    public string BusName { get; }

    public string Location { get; }

    public static LinuxI2cSmbusController? TryOpen(string busName, string devicePath, string adapterName)
    {
        try
        {
            var handle = File.OpenHandle(devicePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return new LinuxI2cSmbusController(handle, busName, $"{devicePath} ({adapterName})");
        }
        catch
        {
            return null;
        }
    }

    public unsafe bool TryWriteByteData(byte address, byte register, byte value)
    {
        SmbusSafetyGuard.ValidateWrite(address, register, value, SmbusSafetyGuard.HardMaxAllowedAddress);

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            if (IoctlAddress(_handle, I2cSlave, address) < 0)
            {
                return false;
            }

            SmbusData data = default;
            data.Byte = value;
            var args = new SmbusIoctlData
            {
                ReadWrite = I2cSmbusWrite,
                Command = register,
                Size = I2cSmbusByteData,
                Data = &data,
            };

            return IoctlSmbus(_handle, I2cSmbus, &args) >= 0;
        }
    }

    public unsafe bool TryReadWordData(byte address, byte register, out ushort value)
    {
        SmbusSafetyGuard.ValidateRead(address, register, SmbusSafetyGuard.HardMaxAllowedAddress);
        value = 0;

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            if (IoctlAddress(_handle, I2cSlave, address) < 0)
            {
                return false;
            }

            SmbusData data = default;
            var args = new SmbusIoctlData
            {
                ReadWrite = I2cSmbusRead,
                Command = register,
                Size = I2cSmbusWordData,
                Data = &data,
            };

            if (IoctlSmbus(_handle, I2cSmbus, &args) < 0)
            {
                return false;
            }

            value = data.Word;
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _handle.Dispose();
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 34)]
    private struct SmbusData
    {
        [FieldOffset(0)]
        public byte Byte;

        [FieldOffset(0)]
        public ushort Word;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct SmbusIoctlData
    {
        public byte ReadWrite;
        public byte Command;
        public uint Size;
        public SmbusData* Data;
    }

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int IoctlAddress(SafeFileHandle fd, ulong request, nint arg);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static unsafe partial int IoctlSmbus(SafeFileHandle fd, ulong request, SmbusIoctlData* arg);
}
