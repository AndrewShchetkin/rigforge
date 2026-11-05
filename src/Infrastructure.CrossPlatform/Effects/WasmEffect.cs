using System.Buffers.Binary;
using RigForge.Application.Effects;
using RigForge.Core.Models;
using Wasmtime;

namespace RigForge.Infrastructure.CrossPlatform.Effects;

/// <summary>
/// One running guest. The <see cref="Store"/> is created on the first sample and stays on that thread.
/// The catalog owns the <see cref="Engine"/> and <see cref="Module"/>.
/// </summary>
internal sealed class WasmEffect : IEffect
{
    private readonly Engine _engine;
    private readonly Module _module;
    private Store? _store;
    private Memory? _memory;
    private Func<int>? _sample;
    private bool _failed;

    public WasmEffect(EffectDescriptor descriptor, Engine engine, Module module)
    {
        Descriptor = descriptor;
        _engine = engine;
        _module = module;
    }

    /// <summary>
    /// Columns of the preview grid. One row is one <c>sample</c> call so a full canvas stays inside the fuel budget.
    /// </summary>
    private const int PreviewColumns = 40;

    /// <summary>Rows of the preview grid. 40×23 covers a 320×180 canvas with about eight pixels per cell.</summary>
    private const int PreviewRows = 23;

    private LedPoint[]? _previewPoints;
    private RgbColor[]? _previewColors;

    public EffectDescriptor Descriptor { get; }

    /// <summary>
    /// Paints a coarse grid through <c>sample</c> and stretches each cell over the canvas.
    /// The device path keeps calling <see cref="TrySamplePoints"/> at the real LED coordinates.
    /// </summary>
    public void Render(Span<byte> rgba, in EffectFrameContext context)
    {
        var width = context.Width;
        var height = context.Height;
        if (width <= 0 || height <= 0 || rgba.Length < width * height * 4)
        {
            return;
        }

        var count = PreviewColumns * PreviewRows;
        if (_previewPoints is null || _previewPoints.Length != count)
        {
            _previewPoints = new LedPoint[count];
            _previewColors = new RgbColor[count];
        }

        var points = _previewPoints;
        var colors = _previewColors!;
        for (var row = 0; row < PreviewRows; row++)
        {
            var y = (row + 0.5f) * height / PreviewRows;
            var start = row * PreviewColumns;
            for (var column = 0; column < PreviewColumns; column++)
            {
                points[start + column] = new LedPoint((column + 0.5f) * width / PreviewColumns, y, true);
            }

            Sample(points.AsSpan(start, PreviewColumns), colors.AsSpan(start, PreviewColumns), in context);
        }

        for (var y = 0; y < height; y++)
        {
            var gridRow = Math.Min(y * PreviewRows / height, PreviewRows - 1) * PreviewColumns;
            var destination = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var color = colors[gridRow + Math.Min(x * PreviewColumns / width, PreviewColumns - 1)];
                rgba[destination++] = color.R;
                rgba[destination++] = color.G;
                rgba[destination++] = color.B;
                rgba[destination++] = 255;
            }
        }
    }

    public bool TrySamplePoints(ReadOnlySpan<LedPoint> points, Span<RgbColor> destination, in EffectFrameContext context)
    {
        if (_failed)
        {
            destination.Fill(default);
            return true;
        }

        try
        {
            Sample(points, destination, in context);
            return true;
        }
        catch (Exception)
        {
            _failed = true;
            throw;
        }
    }

    public void Dispose()
    {
        _store?.Dispose();
        _store = null;
        _memory = null;
        _sample = null;
    }

    private void Sample(ReadOnlySpan<LedPoint> points, Span<RgbColor> destination, in EffectFrameContext context)
    {
        if (destination.Length < points.Length)
        {
            throw new ArgumentException("The destination is shorter than the LED point list.", nameof(destination));
        }

        var frameBytes = WasmGuestLayout.FrameBytes(points.Length);
        if (frameBytes > WasmGuestLayout.MaxMemoryBytes)
        {
            throw new InvalidOperationException(
                $"Effect '{Descriptor.Id}' was asked to sample {points.Length} LEDs, which does not fit in {WasmGuestLayout.MaxMemoryPages} memory pages.");
        }

        EnsureInstance();
        EnsureCapacity(frameBytes);

        var memory = _memory!;
        var span = memory.GetSpan(0, frameBytes);
        WriteFrame(span, points, in context);

        _store!.Fuel = WasmGuestLayout.SampleFuel;
        var status = _sample!();
        if (status != 0)
        {
            throw new InvalidOperationException($"Effect '{Descriptor.Id}' sample returned {status}.");
        }

        // The guest may have grown the memory, which invalidates the span taken above.
        span = memory.GetSpan(0, frameBytes);
        var colors = span.Slice(WasmGuestLayout.PointsOffset + points.Length * WasmGuestLayout.PointStride);
        for (var i = 0; i < points.Length; i++)
        {
            if (!points[i].Enabled)
            {
                destination[i] = default;
                continue;
            }

            var packed = BinaryPrimitives.ReadUInt32LittleEndian(colors.Slice(i * WasmGuestLayout.ColorStride));
            destination[i] = new RgbColor((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
        }
    }

    private void EnsureInstance()
    {
        if (_sample is not null)
        {
            return;
        }

        var store = WasmEffectHost.CreateStore(_engine);
        try
        {
            store.Fuel = WasmGuestLayout.InstantiateFuel;
            var instance = new Instance(store, _module);
            var memory = instance.GetMemory(WasmGuestLayout.MemoryExport)
                ?? throw new InvalidOperationException($"Effect '{Descriptor.Id}' does not export memory.");
            var sample = instance.GetFunction<int>(WasmGuestLayout.SampleExport)
                ?? throw new InvalidOperationException($"Effect '{Descriptor.Id}' does not export sample() -> i32.");
            _store = store;
            _memory = memory;
            _sample = sample;
        }
        catch
        {
            store.Dispose();
            throw;
        }
    }

    private void EnsureCapacity(int frameBytes)
    {
        var memory = _memory!;
        if (memory.GetLength() >= frameBytes)
        {
            return;
        }

        var pagesNeeded = (frameBytes + WasmGuestLayout.PageSize - 1) / WasmGuestLayout.PageSize;
        var delta = pagesNeeded - memory.GetSize();
        if (delta > 0)
        {
            memory.Grow(delta);
        }
    }

    private static void WriteFrame(Span<byte> span, ReadOnlySpan<LedPoint> points, in EffectFrameContext context)
    {
        BinaryPrimitives.WriteDoubleLittleEndian(span[WasmGuestLayout.TimeOffset..], context.Time);
        BinaryPrimitives.WriteSingleLittleEndian(span[WasmGuestLayout.SpeedOffset..], context.Speed);
        BinaryPrimitives.WriteSingleLittleEndian(span[WasmGuestLayout.AngleOffset..], context.Angle);
        BinaryPrimitives.WriteInt32LittleEndian(span[WasmGuestLayout.WidthOffset..], context.Width);
        BinaryPrimitives.WriteInt32LittleEndian(span[WasmGuestLayout.HeightOffset..], context.Height);

        var colorCount = Math.Min(WasmGuestLayout.MaxColors, context.Colors.Count);
        span.Slice(WasmGuestLayout.ColorsOffset, WasmGuestLayout.MaxColors * WasmGuestLayout.ColorStride).Clear();
        BinaryPrimitives.WriteInt32LittleEndian(span[WasmGuestLayout.ColorCountOffset..], colorCount);
        BinaryPrimitives.WriteInt32LittleEndian(span[WasmGuestLayout.PointCountOffset..], points.Length);
        for (var i = 0; i < colorCount; i++)
        {
            var color = context.Colors[i];
            var packed = ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
            BinaryPrimitives.WriteUInt32LittleEndian(span[(WasmGuestLayout.ColorsOffset + i * WasmGuestLayout.ColorStride)..], packed);
        }

        span.Slice(WasmGuestLayout.PointsOffset + points.Length * WasmGuestLayout.PointStride, points.Length * WasmGuestLayout.ColorStride).Clear();
        for (var i = 0; i < points.Length; i++)
        {
            var at = WasmGuestLayout.PointsOffset + i * WasmGuestLayout.PointStride;
            BinaryPrimitives.WriteSingleLittleEndian(span[at..], points[i].X);
            BinaryPrimitives.WriteSingleLittleEndian(span[(at + 4)..], points[i].Y);
        }
    }
}
