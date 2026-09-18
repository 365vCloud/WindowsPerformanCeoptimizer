using WPO.Domain.Models;

namespace WPO.Core.Cleanup;

/// <summary>
/// Builds a non-destructive preview of cleanup candidates. Never deletes,
/// moves, or otherwise mutates any file; purely aggregates scanner output.
/// </summary>
public interface ICleanupPreviewService
{
    Task<CleanupPreviewResult> BuildPreviewAsync(CancellationToken cancellationToken);
}
