using WPO.Domain.Enums;

namespace WPO.Domain.Models;

/// <summary>
/// Outcome of executing deletion for a single previously-scanned item.
/// </summary>
public sealed record CleanupExecutionItemResult
{
    public required Guid ItemId { get; init; }

    public required string FullPath { get; init; }

    public required CleanupItemStatus Status { get; init; }

    public required long SizeBytes { get; init; }

    public string? ErrorMessage { get; init; }
}
