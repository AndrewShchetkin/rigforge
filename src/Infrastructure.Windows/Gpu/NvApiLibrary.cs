using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using RigForge.Infrastructure.CrossPlatform.Gpu;

namespace RigForge.Infrastructure.Windows.Gpu;

/// <summary>
/// <c>NV_I2C_INFO_V3</c> as declared in NVIDIA's <c>nvapi.h</c> (x64, 8-byte packing, 64 bytes). Only ever filled for the
/// GPU's internal RGB port: DDC ports are never addressed (<see cref="IsDdcPort"/> stays 0).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal unsafe struct NvI2cInfoV3
{
    /// <summary>NVAPI struct version: <c>sizeof(NV_I2C_INFO_V3) | (3 &lt;&lt; 16)</c>.</summary>
    public const uint CurrentVersion = 64u | (3u << 16);

    public const uint SpeedDeprecated = 0xFFFF;
    public const uint SpeedDefault = 0;
    public const byte RgbPortId = 1;

    public uint Version;
    public uint DisplayMask;
    public byte IsDdcPort;
    public byte I2cDevAddress;
    public byte* PbI2cRegAddress;
    public uint RegAddrSize;
    public byte* PbData;
    public uint Size;
    public uint I2cSpeed;
    public uint I2cSpeedKhz;
    public byte PortId;
    public uint IsPortIdSet;

    /// <summary>Header for the internal RGB port (port id 1, never DDC) addressing the 7-bit <paramref name="slaveAddress"/>.</summary>
    public static NvI2cInfoV3 ForRgbPort(byte slaveAddress) => new()
    {
        Version = CurrentVersion,
        DisplayMask = 0,
        IsDdcPort = 0,
        I2cDevAddress = (byte)(slaveAddress << 1),
        I2cSpeed = SpeedDeprecated,
        I2cSpeedKhz = SpeedDefault,
        PortId = RgbPortId,
        IsPortIdSet = 1,
    };
}

/// <summary>Unpacks the words returned by <c>NvAPI_GPU_GetPCIIdentifiers</c>.</summary>
internal static class NvApiPciIds
{
    /// <summary><paramref name="deviceId"/> = <c>(device &lt;&lt; 16) | vendor</c>; <paramref name="subSystemId"/> = <c>(subDevice &lt;&lt; 16) | subVendor</c>.</summary>
    public static GpuPciIdentity Unpack(uint deviceId, uint subSystemId) => new(
        VendorId: (ushort)(deviceId & 0xFFFF),
        DeviceId: (ushort)(deviceId >> 16),
        SubVendorId: (ushort)(subSystemId & 0xFFFF),
        SubDeviceId: (ushort)(subSystemId >> 16));
}

/// <summary>
/// User-mode binding to the NVIDIA driver's own <c>nvapi64.dll</c> (installed in System32 with the display driver, no
/// administrator rights or kernel driver needed). Every entry point is resolved through the single exported
/// <c>nvapi_QueryInterface</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class NvApiLibrary
{
    public const int MaxPhysicalGpus = 64;
    public const int ShortStringMax = 64;
    public const int StatusOk = 0;

    private const uint IdInitialize = 0x0150E828u;
    private const uint IdEnumPhysicalGpus = 0xE5AC921Fu;
    private const uint IdGpuGetFullName = 0xCEEE8E9Fu;
    private const uint IdGpuGetPciIdentifiers = 0x2DDFB66Eu;
    // Verified live on an RTX 5070 Ti: 0x2FDE12C5 is the legacy NvAPI_I2CRead (returns NVAPI_NOT_SUPPORTED, -104, for
    // register reads on this driver); the extended read is 0x4D7B0709. GetBusId is 0x1BE0B8E5, GetBusSlotId 0x2A0A350F.
    private const uint IdGpuGetBusId = 0x1BE0B8E5u;
    private const uint IdGpuGetBusSlotId = 0x2A0A350Fu;
    private const uint IdI2cReadEx = 0x4D7B0709u;
    private const uint IdI2cWriteEx = 0x283AC65Au;

    private static readonly Lock Gate = new();
    private static NvApiLibrary? s_instance;
    private static string? s_failure;
    private static bool s_attempted;

    private readonly delegate* unmanaged[Cdecl]<nint*, uint*, int> _enumPhysicalGpus;
    private readonly delegate* unmanaged[Cdecl]<nint, byte*, int> _getFullName;
    private readonly delegate* unmanaged[Cdecl]<nint, uint*, uint*, uint*, uint*, int> _getPciIdentifiers;
    private readonly delegate* unmanaged[Cdecl]<nint, uint*, int> _getBusId;
    private readonly delegate* unmanaged[Cdecl]<nint, uint*, int> _getBusSlotId;
    private readonly delegate* unmanaged[Cdecl]<nint, NvI2cInfoV3*, uint*, int> _i2cReadEx;
    private readonly delegate* unmanaged[Cdecl]<nint, NvI2cInfoV3*, uint*, int> _i2cWriteEx;

    private NvApiLibrary(
        delegate* unmanaged[Cdecl]<nint*, uint*, int> enumPhysicalGpus,
        delegate* unmanaged[Cdecl]<nint, byte*, int> getFullName,
        delegate* unmanaged[Cdecl]<nint, uint*, uint*, uint*, uint*, int> getPciIdentifiers,
        delegate* unmanaged[Cdecl]<nint, uint*, int> getBusId,
        delegate* unmanaged[Cdecl]<nint, uint*, int> getBusSlotId,
        delegate* unmanaged[Cdecl]<nint, NvI2cInfoV3*, uint*, int> i2cReadEx,
        delegate* unmanaged[Cdecl]<nint, NvI2cInfoV3*, uint*, int> i2cWriteEx)
    {
        _enumPhysicalGpus = enumPhysicalGpus;
        _getFullName = getFullName;
        _getPciIdentifiers = getPciIdentifiers;
        _getBusId = getBusId;
        _getBusSlotId = getBusSlotId;
        _i2cReadEx = i2cReadEx;
        _i2cWriteEx = i2cWriteEx;
    }

    /// <summary>Loads and initializes NVAPI once per process. Returns null (with a reason) on machines without an NVIDIA driver.</summary>
    public static NvApiLibrary? TryGet(out string status)
    {
        lock (Gate)
        {
            if (!s_attempted)
            {
                s_attempted = true;
                s_instance = Load(out s_failure);
            }

            status = s_instance is null
                ? s_failure ?? "NVAPI is unavailable."
                : "NVAPI (nvapi64.dll) ready; user-mode, no administrator rights required.";
            return s_instance;
        }
    }

    /// <summary>Returns the physical GPU handles in NVAPI enumeration order.</summary>
    public IReadOnlyList<nint> EnumeratePhysicalGpus()
    {
        var handles = stackalloc nint[MaxPhysicalGpus];
        uint count = 0;
        if (_enumPhysicalGpus(handles, &count) != StatusOk)
        {
            return [];
        }

        var result = new nint[Math.Min(count, (uint)MaxPhysicalGpus)];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = handles[i];
        }

        return result;
    }

    public string GetFullName(nint gpu)
    {
        var buffer = stackalloc byte[ShortStringMax];
        new Span<byte>(buffer, ShortStringMax).Clear();
        if (_getFullName(gpu, buffer) != StatusOk)
        {
            return string.Empty;
        }

        var span = new ReadOnlySpan<byte>(buffer, ShortStringMax);
        var length = span.IndexOf((byte)0);
        return Encoding.ASCII.GetString(length < 0 ? span : span[..length]).Trim();
    }

    public bool TryGetPciIdentity(nint gpu, out GpuPciIdentity identity)
    {
        uint deviceId = 0;
        uint subSystemId = 0;
        uint revisionId = 0;
        uint extDeviceId = 0;
        if (_getPciIdentifiers(gpu, &deviceId, &subSystemId, &revisionId, &extDeviceId) != StatusOk)
        {
            identity = default;
            return false;
        }

        identity = NvApiPciIds.Unpack(deviceId, subSystemId);
        return true;
    }

    public bool TryGetBusId(nint gpu, out uint busId)
    {
        uint value = 0;
        if (_getBusId != null && _getBusId(gpu, &value) == StatusOk)
        {
            busId = value;
            return true;
        }

        busId = 0;
        return false;
    }

    public bool TryGetBusSlotId(nint gpu, out uint slotId)
    {
        uint value = 0;
        if (_getBusSlotId != null && _getBusSlotId(gpu, &value) == StatusOk)
        {
            slotId = value;
            return true;
        }

        slotId = 0;
        return false;
    }

    /// <summary>Runs <c>NvAPI_I2CReadEx</c>/<c>NvAPI_I2CWriteEx</c>; returns the raw NVAPI status (0 = OK).</summary>
    public int I2cTransfer(nint gpu, bool write, NvI2cInfoV3* info)
    {
        // NvAPI_I2C*Ex takes a trailing in/out word that OpenRGB and SignalRGB always pass as zero.
        uint unknown = 0;
        return write ? _i2cWriteEx(gpu, info, &unknown) : _i2cReadEx(gpu, info, &unknown);
    }

    private static NvApiLibrary? Load(out string? failure)
    {
        if (!NativeLibrary.TryLoad("nvapi64.dll", typeof(NvApiLibrary).Assembly, DllImportSearchPath.System32, out var handle))
        {
            failure = "nvapi64.dll was not found; the NVIDIA display driver is not installed.";
            return null;
        }

        if (!NativeLibrary.TryGetExport(handle, "nvapi_QueryInterface", out var queryAddress) || queryAddress == nint.Zero)
        {
            failure = "nvapi64.dll does not export nvapi_QueryInterface.";
            return null;
        }

        var query = (delegate* unmanaged[Cdecl]<uint, nint>)queryAddress;

        var initialize = (delegate* unmanaged[Cdecl]<int>)query(IdInitialize);
        if (initialize == null)
        {
            failure = "NvAPI_Initialize is not exposed by this NVIDIA driver.";
            return null;
        }

        var initStatus = initialize();
        if (initStatus != StatusOk)
        {
            failure = $"NvAPI_Initialize failed with status {initStatus}.";
            return null;
        }

        var enumerate = (delegate* unmanaged[Cdecl]<nint*, uint*, int>)query(IdEnumPhysicalGpus);
        var fullName = (delegate* unmanaged[Cdecl]<nint, byte*, int>)query(IdGpuGetFullName);
        var pciIds = (delegate* unmanaged[Cdecl]<nint, uint*, uint*, uint*, uint*, int>)query(IdGpuGetPciIdentifiers);
        var read = (delegate* unmanaged[Cdecl]<nint, NvI2cInfoV3*, uint*, int>)query(IdI2cReadEx);
        var write = (delegate* unmanaged[Cdecl]<nint, NvI2cInfoV3*, uint*, int>)query(IdI2cWriteEx);
        if (enumerate == null || fullName == null || pciIds == null || read == null || write == null)
        {
            failure = "This NVIDIA driver does not expose the NVAPI GPU enumeration / I2C entry points.";
            return null;
        }

        failure = null;
        return new NvApiLibrary(
            enumerate,
            fullName,
            pciIds,
            (delegate* unmanaged[Cdecl]<nint, uint*, int>)query(IdGpuGetBusId),
            (delegate* unmanaged[Cdecl]<nint, uint*, int>)query(IdGpuGetBusSlotId),
            read,
            write);
    }
}
