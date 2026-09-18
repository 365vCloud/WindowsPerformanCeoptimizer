using WPO.Domain.Models;

namespace WPO.Core.Cleanup;

/// <summary>
/// Executes deletion of a user-confirmed selection from a prior preview.
/// Always defaults to Recycle Bin; permanent deletion requires an explicit
/// second confirmation flag on the selection itself.
/// </summary>
public interface ICleanupExecutionService
{
    Task<CleanupExecutionResult> ExecuteAsync(
        CleanupPreviewResult preview,
        CleanupSelection selection,
        CancellationToken cancellationToken);
}
