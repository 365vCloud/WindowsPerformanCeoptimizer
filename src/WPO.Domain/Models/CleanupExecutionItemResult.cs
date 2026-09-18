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

    /// <summary>Whether this item was explicitly selected for this execution.</summary>
    public required bool WasSelected { get; init; }

    /// <summary>Stable machine-readable reason for a non-success result.</summary>
    public CleanupExecutionReason Reason { get; init; }

    public string? ErrorMessage { get; init; }
}
