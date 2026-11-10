using Microsoft.Extensions.Logging;
using RigForge.Application.Effects.Library;
using RigForge.Core.Interfaces;
using RigForge.Core.Models;

namespace RigForge.Application.Effects;

/// <summary>Catalog of the built-in effects, plus any external effects that do not reuse a built-in id.</summary>
public sealed partial class EffectRegistry
{
    public const string FallbackEffectId = RainbowWaveEffect.EffectId;

    private readonly Dictionary<string, (EffectDescriptor Descriptor, Func<IEffect> Create)> _effects;

    public EffectRegistry(IScreenCapturer screenCapturer, IExternalEffectCatalog? external = null, ILogger<EffectRegistry>? logger = null)
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
            [AuroraEffect.EffectId] = (AuroraEffect.Info, () => new AuroraEffect()),
            [SpiralVortexEffect.EffectId] = (SpiralVortexEffect.Info, () => new SpiralVortexEffect()),
            [RadialPulseEffect.EffectId] = (RadialPulseEffect.Info, () => new RadialPulseEffect()),
            [CyberWaveEffect.EffectId] = (CyberWaveEffect.Info, () => new CyberWaveEffect()),
            [PlasmaStormEffect.EffectId] = (PlasmaStormEffect.Info, () => new PlasmaStormEffect()),
            [FireLavaEffect.EffectId] = (FireLavaEffect.Info, () => new FireLavaEffect()),
            [MatrixRainEffect.EffectId] = (MatrixRainEffect.Info, () => new MatrixRainEffect()),
            [StarlightEffect.EffectId] = (StarlightEffect.Info, () => new StarlightEffect()),
            [MeteorScannerEffect.EffectId] = (MeteorScannerEffect.Info, () => new MeteorScannerEffect()),
            [AudioReactiveEffect.EffectId] = (AudioReactiveEffect.Info, () => new AudioReactiveEffect()),
        };

        if (external is null)
        {
            return;
        }

        foreach (var descriptor in external.Descriptors)
        {
            if (_effects.ContainsKey(descriptor.Id))
            {
                if (logger is not null)
                {
                    LogBuiltInIdIgnored(logger, descriptor.Id);
                }

                continue;
            }

            var effectId = descriptor.Id;
            _effects[effectId] = (descriptor, () => external.TryCreate(effectId, out var effect)
                ? effect
                : throw new InvalidOperationException($"External effect '{effectId}' could not be created."));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "External effect {EffectId} matches a built-in effect and was ignored")]
    private static partial void LogBuiltInIdIgnored(ILogger logger, string effectId);

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
