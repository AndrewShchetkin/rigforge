namespace RigForge.Application.Effects;

/// <summary>
/// Effects loaded from outside the built-in library. The host compiles them lazily and never grants them
/// filesystem or network access.
/// </summary>
public interface IExternalEffectCatalog
{
    IReadOnlyList<EffectDescriptor> Descriptors { get; }

    /// <summary>Creates a fresh instance. Returns false when <paramref name="effectId"/> is not in this catalog.</summary>
    bool TryCreate(string effectId, out IEffect effect);
}
