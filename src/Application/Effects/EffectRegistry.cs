using RigForge.Application.Effects.Library;
using RigForge.Core.Interfaces;

namespace RigForge.Application.Effects;

/// <summary>Catalog of the built-in effects.</summary>
public sealed class EffectRegistry
{
    public const string FallbackEffectId = RainbowWaveEffect.EffectId;

    private readonly Dictionary<string, (EffectDescriptor Descriptor, Func<IEffect> Create)> _effects;

    public EffectRegistry(IScreenCapturer screenCapturer)
    {
        _effects = new(StringComparer.OrdinalIgnoreCase)
        {
            [RainbowWaveEffect.EffectId] = (RainbowWaveEffect.Info, () => new RainbowWaveEffect()),
            [GradientWaveEffect.EffectId] = (GradientWaveEffect.Info, () => new GradientWaveEffect()),
            [SolidColorEffect.EffectId] = (SolidColorEffect.Info, () => new SolidColorEffect()),
            [BreathingEffect.EffectId] = (BreathingEffect.Info, () => new BreathingEffect()),
            [SpectrumCycleEffect.EffectId] = (SpectrumCycleEffect.Info, () => new SpectrumCycleEffect()),
            [TemperatureEffect.EffectId] = (TemperatureEffect.Info, () => new TemperatureEffect()),
            [AmbilightEffect.EffectId] = (AmbilightEffect.Info, () => new AmbilightEffect(screenCapturer)),
        };
    }

    public IReadOnlyList<EffectDescriptor> Descriptors => _effects.Values.Select(e => e.Descriptor).ToArray();

    public bool Contains(string effectId) => _effects.ContainsKey(effectId);

    public IEffect Create(string effectId) =>
        (_effects.TryGetValue(effectId, out var effect) ? effect : _effects[FallbackEffectId]).Create();
}
