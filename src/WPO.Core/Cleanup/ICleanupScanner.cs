using WPO.Domain.Models;

namespace WPO.Core.Cleanup;

/// <summary>
/// Discovers cleanup candidates for a single category. Implementations must
/// only return items whose path has already passed <see cref="Security.IPathSafetyValidator"/>;
/// they are responsible for filtering out anything the validator rejects.
/// </summary>
public interface ICleanupScanner
{
    Domain.Enums.CleanupCategory Category { get; }

    Task<IReadOnlyList<CleanupItem>> ScanAsync(CancellationToken cancellationToken);
}
