using WPO.Domain.Models;

namespace WPO.Core.Export;

/// <summary>Exports immutable cleanup reports without exposing file I/O to the UI.</summary>
public interface IExportService
{
    Task ExportCleanupResultAsync(
        CleanupExecutionResult result,
        string destinationPath,
        ExportFormat format,
        CancellationToken cancellationToken = default);
}
