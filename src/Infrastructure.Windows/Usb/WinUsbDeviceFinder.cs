using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace RigForge.Infrastructure.Windows.Usb;

/// <summary>
/// Locates device interface paths of WinUSB-bound USB functions with SetupAPI. The interface GUID is read from the
/// function's "DeviceInterfaceGUIDs" registry value (written by the INF or MS OS descriptors), and the serial number is
/// matched against the composite parent ("USB\VID_xxxx&amp;PID_xxxx\SERIAL").
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class WinUsbDeviceFinder
{
    private const uint DigcfPresent = 0x02;
    private const uint DigcfAllClasses = 0x04;
    private const uint DigcfDeviceInterface = 0x10;
    private const uint DicsFlagGlobal = 0x01;
    private const uint DiregDev = 0x01;
    private const uint KeyRead = 0x20019;
    private const int MaxIdLength = 512;

    public static IReadOnlyList<string> FindInterfacePaths(string hardwareId, string? serialNumber)
    {
        var paths = new List<string>();
        var set = SetupDiGetClassDevsW(null, "USB", 0, DigcfPresent | DigcfAllClasses);
        if (set == -1)
        {
            return paths;
        }

        try
        {
            var info = new DevInfoData { Size = (uint)sizeof(DevInfoData) };
            for (uint index = 0; SetupDiEnumDeviceInfo(set, index, ref info); index++)
            {
                var instanceId = GetInstanceId(set, ref info);
                if (instanceId is null || !instanceId.Contains(hardwareId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(serialNumber) && !MatchesSerial(info.DevInst, instanceId, serialNumber))
                {
                    continue;
                }

                foreach (var guid in ReadInterfaceGuids(set, ref info))
                {
                    paths.AddRange(InterfacePaths(guid, instanceId));
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool MatchesSerial(uint devInst, string instanceId, string serial)
    {
        if (instanceId.EndsWith(serial, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return CM_Get_Parent(out var parent, devInst, 0) == 0
            && GetDeviceId(parent) is { } parentId
            && parentId.EndsWith(serial, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<Guid> ReadInterfaceGuids(nint set, ref DevInfoData info)
    {
        var rawKey = SetupDiOpenDevRegKey(set, ref info, DicsFlagGlobal, 0, DiregDev, KeyRead);
        if (rawKey == -1)
        {
            return [];
        }

        using var key = RegistryKey.FromHandle(new SafeRegistryHandle(rawKey, ownsHandle: true));
        var values = key.GetValue("DeviceInterfaceGUIDs") switch
        {
            string[] many => many,
            string one => [one],
            _ => key.GetValue("DeviceInterfaceGUID") is string single ? [single] : [],
        };

        return values.Select(v => Guid.TryParse(v, out var guid) ? guid : Guid.Empty).Where(g => g != Guid.Empty).ToArray();
    }

    private static List<string> InterfacePaths(Guid interfaceGuid, string instanceId)
    {
        var paths = new List<string>();
        var set = SetupDiGetClassDevsW(&interfaceGuid, null, 0, DigcfPresent | DigcfDeviceInterface);
        if (set == -1)
        {
            return paths;
        }

        try
        {
            var interfaceData = new DeviceInterfaceData { Size = (uint)sizeof(DeviceInterfaceData) };
            for (uint index = 0; SetupDiEnumDeviceInterfaces(set, 0, &interfaceGuid, index, ref interfaceData); index++)
            {
                var info = new DevInfoData { Size = (uint)sizeof(DevInfoData) };
                SetupDiGetDeviceInterfaceDetailW(set, ref interfaceData, null, 0, out var required, ref info);
                if (required == 0)
                {
                    continue;
                }

                var buffer = new byte[required];
                fixed (byte* detail = buffer)
                {
                    // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize: 8 on 64-bit, 6 on 32-bit; the path starts at offset 4.
                    *(uint*)detail = IntPtr.Size == 8 ? 8u : 6u;
                    if (!SetupDiGetDeviceInterfaceDetailW(set, ref interfaceData, detail, required, out _, ref info))
                    {
                        continue;
                    }

                    if (string.Equals(GetInstanceId(set, ref info), instanceId, StringComparison.OrdinalIgnoreCase))
                    {
                        paths.Add(new string((char*)(detail + 4)));
                    }
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return paths;
    }

    private static string? GetInstanceId(nint set, ref DevInfoData info)
    {
        var buffer = stackalloc char[MaxIdLength];
        return SetupDiGetDeviceInstanceIdW(set, ref info, buffer, MaxIdLength, out _) ? new string(buffer) : null;
    }

    private static string? GetDeviceId(uint devInst)
    {
        var buffer = stackalloc char[MaxIdLength];
        return CM_Get_Device_IDW(devInst, buffer, MaxIdLength, 0) == 0 ? new string(buffer) : null;
    }

    [LibraryImport("setupapi.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SetupDiGetClassDevsW(Guid* classGuid, string? enumerator, nint parent, uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiEnumDeviceInfo(nint set, uint index, ref DevInfoData info);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiGetDeviceInstanceIdW(nint set, ref DevInfoData info, char* buffer, uint bufferSize, out uint requiredSize);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    private static partial nint SetupDiOpenDevRegKey(nint set, ref DevInfoData info, uint scope, uint hardwareProfile, uint keyType, uint desiredAccess);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiEnumDeviceInterfaces(nint set, nint info, Guid* interfaceGuid, uint index, ref DeviceInterfaceData data);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiGetDeviceInterfaceDetailW(nint set, ref DeviceInterfaceData data, byte* detail, uint detailSize, out uint requiredSize, ref DevInfoData info);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiDestroyDeviceInfoList(nint set);

    [LibraryImport("cfgmgr32.dll")]
    private static partial uint CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    private static partial uint CM_Get_Device_IDW(uint devInst, char* buffer, uint length, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct DevInfoData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DevInst;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        public uint Size;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public nint Reserved;
    }
}
