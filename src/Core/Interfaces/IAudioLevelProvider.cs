namespace RigForge.Core.Interfaces;

/// <summary>Live audio levels for effects that react to sound. The lighting loop calls this only when the active effect asks for it.</summary>
public interface IAudioLevelProvider : IDisposable
{
    bool IsSupported { get; }

    /// <summary>
    /// Writes up to 16 normalized frequency bands (0..1, bass to treble) into <paramref name="bands"/>
    /// and returns overall RMS level (0..1). Implementations must be thread-safe, lazy-started on first read,
    /// and zero-allocation per call.
    /// </summary>
    float ReadSpectrum(Span<float> bands);
}
