using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RigForge.Infrastructure.Windows.Capture;

/// <summary>
/// One Desktop Duplication session for a monitor. The desktop texture is copied into a mip-mapped texture,
/// downscaled by GenerateMips on the GPU, and only the small mip level is read back to the CPU.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DuplicationSession : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGIOutputDuplication _duplication;
    private readonly ID3D11Texture2D _mipTexture;
    private readonly ID3D11ShaderResourceView _mipView;
    private readonly ID3D11Texture2D _staging;
    private readonly uint _mipLevel;
    private readonly byte[] _pixels;

    public DuplicationSession(int outputIndex, int targetWidth)
    {
        using var adapter = FindAdapter(outputIndex, out var localOutputIndex);
        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, [FeatureLevel.Level_11_0], out ID3D11Device device, out ID3D11DeviceContext context)
            .CheckError();
        _device = device;
        _context = context;

        try
        {
            adapter.EnumOutputs(localOutputIndex, out var output).CheckError();
            using (output)
            using (var output1 = output.QueryInterface<IDXGIOutput1>())
            {
                _duplication = output1.DuplicateOutput(_device);
            }

            var mode = _duplication.Description.ModeDescription;
            SourceWidth = (int)mode.Width;
            SourceHeight = (int)mode.Height;

            uint level = 0;
            while (level < 10 && SourceWidth >> (int)(level + 1) >= targetWidth)
            {
                level++;
            }

            _mipLevel = level;
            Width = Math.Max(1, SourceWidth >> (int)level);
            Height = Math.Max(1, SourceHeight >> (int)level);

            _mipTexture = _device.CreateTexture2D(new Texture2DDescription
            {
                Width = mode.Width,
                Height = mode.Height,
                MipLevels = level + 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                MiscFlags = ResourceOptionFlags.GenerateMips,
            });
            _mipView = _device.CreateShaderResourceView(_mipTexture);
            _staging = _device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)Width,
                Height = (uint)Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                CPUAccessFlags = CpuAccessFlags.Read,
            });
            _pixels = new byte[Width * Height * 4];
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public int SourceWidth { get; }

    public int SourceHeight { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Returns the downscaled BGRA frame, or null when the desktop did not change within the timeout.</summary>
    public byte[]? TryCapture(uint timeoutMilliseconds)
    {
        var result = _duplication.AcquireNextFrame(timeoutMilliseconds, out var frameInfo, out var resource);
        if (result == Vortice.DXGI.ResultCode.WaitTimeout)
        {
            return null;
        }

        result.CheckError();
        try
        {
            // LastPresentTime == 0: only the mouse pointer moved.
            if (frameInfo.LastPresentTime == 0)
            {
                return null;
            }

            using var texture = resource.QueryInterface<ID3D11Texture2D>();
            _context.CopySubresourceRegion(_mipTexture, 0, 0, 0, 0, texture, 0);
            _context.GenerateMips(_mipView);
            _context.CopySubresourceRegion(_staging, 0, 0, 0, 0, _mipTexture, _mipLevel);

            _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out var mapped).CheckError();
            try
            {
                var rowBytes = Width * 4;
                for (var y = 0; y < Height; y++)
                {
                    Marshal.Copy(mapped.DataPointer + (nint)(y * mapped.RowPitch), _pixels, y * rowBytes, rowBytes);
                }
            }
            finally
            {
                _context.Unmap(_staging, 0);
            }

            return _pixels;
        }
        finally
        {
            resource.Dispose();
            _duplication.ReleaseFrame();
        }
    }

    public void Dispose()
    {
        _staging?.Dispose();
        _mipView?.Dispose();
        _mipTexture?.Dispose();
        _duplication?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
    }

    /// <summary>Finds the adapter that owns the n-th monitor across all adapters (hybrid iGPU + dGPU systems).</summary>
    private static IDXGIAdapter1 FindAdapter(int outputIndex, out uint localOutputIndex)
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        var remaining = outputIndex;
        for (uint adapterIndex = 0; factory.EnumAdapters1(adapterIndex, out var adapter).Success; adapterIndex++)
        {
            uint outputs = 0;
            while (adapter.EnumOutputs(outputs, out var output).Success)
            {
                output.Dispose();
                outputs++;
            }

            if (remaining < outputs)
            {
                localOutputIndex = (uint)remaining;
                return adapter;
            }

            remaining -= (int)outputs;
            adapter.Dispose();
        }

        throw new InvalidOperationException($"Monitor #{outputIndex} was not found on any graphics adapter.");
    }
}
