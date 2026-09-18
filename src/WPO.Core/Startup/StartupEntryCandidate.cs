using WPO.Domain.Enums;

namespace WPO.Core.Startup;

/// <summary>
/// Raw, uninspected autorun entry discovered from a single source (a registry
/// value name or a Startup-folder file). Kept intentionally minimal so the
/// enumeration step (which touches the registry/filesystem) stays separate
/// from the inspection step (which reads file metadata/signature), allowing
/// each to be isolated and unit tested independently.
/// </summary>
public sealed record StartupEntryCandidate
{
    public required string Name { get; init; }

    public required StartupItemSource Source { get; init; }

    /// <summary>Raw registry command string or filesystem path for this entry.</summary>
    public string? RawCommand { get; init; }
}
