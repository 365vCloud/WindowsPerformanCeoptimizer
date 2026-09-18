using WPO.Domain.Models;

namespace WPO.Core.Startup;

/// <summary>
/// Low-level, platform-specific reader used by <see cref="WindowsStartupItemService"/>.
/// Separated from the service so the sorting/limiting/isolation/cancellation
/// logic can be unit tested with a fake reader that never touches the real
/// registry or filesystem.
/// </summary>
public interface IStartupEntryReader
{
    /// <summary>Enumerates candidate entries. Must not throw for a single bad entry/source.</summary>
    IReadOnlyList<StartupEntryCandidate> GetCandidates();

    /// <summary>Inspects a single candidate (path resolution, signature, enabled state).</summary>
    Task<StartupItem> InspectAsync(StartupEntryCandidate candidate, CancellationToken cancellationToken);
}
