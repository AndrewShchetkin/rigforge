using RigForge.Core.Models;

namespace RigForge.Core.Interfaces;

public interface IProfileStore
{
    /// <summary>Returns the saved profile, or null when none exists yet.</summary>
    Task<RigProfile?> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(RigProfile profile, CancellationToken cancellationToken);
}
