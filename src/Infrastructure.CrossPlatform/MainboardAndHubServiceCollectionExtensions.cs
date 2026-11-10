using HidSharp;
using Microsoft.Extensions.DependencyInjection;
using RigForge.Core.Interfaces;
using RigForge.Infrastructure.CrossPlatform.AsRock;
using RigForge.Infrastructure.CrossPlatform.CoolerMaster;
using RigForge.Infrastructure.CrossPlatform.Corsair;
using RigForge.Infrastructure.CrossPlatform.Gigabyte;
using RigForge.Infrastructure.CrossPlatform.Hid;
using RigForge.Infrastructure.CrossPlatform.LianLi;
using RigForge.Infrastructure.CrossPlatform.Msi;

namespace RigForge.Infrastructure.CrossPlatform;

/// <summary>Registers motherboard and ARGB-hub probes. Wave 5 calls this from the cross-platform DI setup.</summary>
internal static class MainboardAndHubServiceCollectionExtensions
{
    public static IServiceCollection AddMainboardAndHubProbes(this IServiceCollection services)
    {
        services.AddSingleton<IDeviceProbe, CorsairDeviceProbe>();
        services.AddSingleton<IDeviceProbe, MsiMysticLightProbe>();
        services.AddSingleton<IDeviceProbe, GigabyteFusion2Probe>();
        services.AddSingleton<IDeviceProbe, AsRockPolychromeProbe>();
        services.AddSingleton<IDeviceProbe, LianLiUniHubProbe>();
        services.AddSingleton<IDeviceProbe, CoolerMasterArgbProbe>();
        return services;
    }
}

/// <summary>
/// Opens a <see cref="HidReportChannel"/> on the output report when it is long enough,
/// otherwise forwards writes to the HID feature report. MSI 185-byte and ITE Fusion controllers need this.
/// </summary>
internal static class HidReportChannelFactory
{
    public static HidReportChannel OpenOutputOrFeature(HidDevice device, int minimumLength)
    {
        var output = SafeLength(() => device.GetMaxOutputReportLength());
        if (output >= minimumLength)
        {
            return HidReportChannel.Open(device);
        }

        var feature = SafeLength(() => device.GetMaxFeatureReportLength());
        if (feature >= minimumLength)
        {
            var input = SafeLength(() => device.GetMaxInputReportLength());
            var stream = device.Open();
            stream.ReadTimeout = 500;
            stream.WriteTimeout = 1000;
            return new HidReportChannel(new HidFeatureReportStream(stream), feature, Math.Max(input, 1));
        }

        return HidReportChannel.Open(device);
    }

    private static int SafeLength(Func<int> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return 0;
        }
    }
}

/// <summary>Presents <see cref="HidStream.SetFeature"/> as a <see cref="Stream.Write"/> so feature-only controllers share <see cref="HidReportChannel"/>.</summary>
internal sealed class HidFeatureReportStream : Stream
{
    private readonly HidStream _inner;

    public HidFeatureReportStream(HidStream inner) => _inner = inner;

    public override bool CanRead => _inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int ReadTimeout
    {
        get => _inner.ReadTimeout;
        set => _inner.ReadTimeout = value;
    }

    public override int WriteTimeout
    {
        get => _inner.WriteTimeout;
        set => _inner.WriteTimeout = value;
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => _inner.SetFeature(buffer, offset, count);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
