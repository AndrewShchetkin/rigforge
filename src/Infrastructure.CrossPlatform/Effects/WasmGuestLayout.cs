namespace RigForge.Infrastructure.CrossPlatform.Effects;

/// <summary>
/// Guest memory written by the host before <c>sample</c> and read back after it.
/// Little-endian. Offsets are part of the module ABI: <c>effects/wasm-plasma.wat</c> uses the same numbers.
/// </summary>
internal static class WasmGuestLayout
{
    public const string MemoryExport = "memory";
    public const string SampleExport = "sample";

    public const int MaxMemoryPages = 32;
    public const int PageSize = 65536;
    public const int MaxMemoryBytes = MaxMemoryPages * PageSize;
    public const int MaxColors = 8;

    public const int TimeOffset = 0;
    public const int SpeedOffset = 8;
    public const int AngleOffset = 12;
    public const int WidthOffset = 16;
    public const int HeightOffset = 20;
    public const int ColorCountOffset = 24;
    public const int PointCountOffset = 28;
    public const int ColorsOffset = 32;
    public const int PointsOffset = 64;
    public const int PointStride = 8;
    public const int ColorStride = 4;

    /// <summary>Budget for module start, separate from the per-frame <see cref="SampleFuel"/>.</summary>
    public const ulong InstantiateFuel = 1_000_000;

    /// <summary>Budget for one <c>sample</c> call. A tight infinite loop traps inside this budget.</summary>
    public const ulong SampleFuel = 2_000_000;

    public static int FrameBytes(int pointCount) =>
        checked(PointsOffset + pointCount * PointStride + pointCount * ColorStride);
}
