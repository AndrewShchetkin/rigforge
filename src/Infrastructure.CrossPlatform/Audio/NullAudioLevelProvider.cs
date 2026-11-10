using RigForge.Core.Interfaces;

namespace RigForge.Infrastructure.CrossPlatform.Audio;

/// <summary>Stand-in used where no capture backend is registered. Reads cost nothing and never start a device.</summary>
public sealed class NullAudioLevelProvider : IAudioLevelProvider
{
    public bool IsSupported => false;

    public float ReadSpectrum(Span<float> bands)
    {
        bands.Clear();
        return 0f;
    }

    public void Dispose()
    {
    }
}
