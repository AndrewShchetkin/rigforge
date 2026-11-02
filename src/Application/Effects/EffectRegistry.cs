using RigForge.Application.Effects.Library;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;

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

    public bool TryGetDescriptor(string effectId, out EffectDescriptor descriptor)
    {
        if (_effects.TryGetValue(effectId, out var effect))
        {
            descriptor = effect.Descriptor;
            return true;
        }

        descriptor = _effects[FallbackEffectId].Descriptor;
        return false;
    }

    /// <summary>Returns settings with every schema parameter of the effect filled in from its default.</summary>
    public EffectSettings FillDefaults(EffectSettings settings) =>
        TryGetDescriptor(settings.EffectId, out var descriptor)
            ? EffectParameterValues.FillDefaults(settings, descriptor)
            : settings;

    public IEffect Create(string effectId) =>
        (_effects.TryGetValue(effectId, out var effect) ? effect : _effects[FallbackEffectId]).Create();
}
