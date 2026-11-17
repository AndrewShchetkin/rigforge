namespace RigForge.Core.Models;

/// <summary>A named list of #RRGGBB colors the user can apply to an effect.</summary>
public sealed record ColorPalette
{
    public required string Id { get; init; }

    public string Name { get; init; } = "";

    public IReadOnlyList<string> Colors { get; init; } = [];
}
