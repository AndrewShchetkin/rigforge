using RigForge.Core.Models;
using SkiaSharp;

namespace RigForge.Application.Effects;

/// <summary>Static metadata used by the UI to build the parameter form.</summary>
public sealed record EffectDescriptor(
    string Id,
    string Name,
    string Description,
    bool IsAnimated,
    bool UsesColors,
    bool UsesSpeed,
    bool UsesAngle,
    bool UsesSensor,
    int MinColors,
    int MaxColors);

/// <summary>Per-frame inputs of an effect.</summary>
/// <param name="Time">Seconds since the effect was activated.</param>
/// <param name="Colors">Parsed <see cref="EffectSettings.Colors"/>.</param>
public readonly record struct EffectFrameContext(
    int Width,
    int Height,
    double Time,
    EffectSettings Settings,
    IReadOnlyList<SKColor> Colors,
    TelemetrySnapshot Telemetry)
{
    public SKColor Color(int index) =>
        Colors.Count == 0 ? SKColors.White : Colors[Math.Clamp(index, 0, Colors.Count - 1)];
}

/// <summary>
/// A procedural (or captured) image drawn onto the virtual canvas every frame. Instances are created when the
/// effect becomes active and disposed when it is replaced, so constructors/Dispose may start/stop resources.
/// </summary>
public interface IEffect : IDisposable
{
    EffectDescriptor Descriptor { get; }

    void Render(SKCanvas canvas, in EffectFrameContext context);
}
