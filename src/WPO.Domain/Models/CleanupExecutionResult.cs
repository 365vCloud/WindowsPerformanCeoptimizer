namespace WPO.Domain.Models;

/// <summary>
/// Aggregate result of a cleanup execution run. <see cref="TotalBytesFreed"/> is
/// computed strictly from items that actually report a Deleted status, never
/// from the originally-selected/candidate size.
/// </summary>
public sealed record CleanupExecutionResult
{
    public required IReadOnlyList<CleanupExecutionItemResult> Items { get; init; }

    public int ScannedItemCount => Items.Count;

    public int SelectedItemCount => Items.Count(i => i.WasSelected);

    public int AttemptedItemCount => Items.Count(i =>
        i.Status is Enums.CleanupItemStatus.Deleted or Enums.CleanupItemStatus.Failed);

    public int SuccessfulItemCount => Items.Count(i => i.Status == Enums.CleanupItemStatus.Deleted);

    public int SkippedItemCount => Items.Count(i => i.Status == Enums.CleanupItemStatus.Skipped);

    public int FailedItemCount => Items.Count(i => i.Status == Enums.CleanupItemStatus.Failed);

    public int CancelledItemCount => Items.Count(i => i.Status == Enums.CleanupItemStatus.Cancelled);

    /// <summary>Bytes selected for this run; this is an estimate, not freed space.</summary>
    public long EstimatedBytes => Items.Where(i => i.WasSelected).Sum(i => i.SizeBytes);

    public long TotalBytesFreed => Items
        .Where(i => i.Status == Enums.CleanupItemStatus.Deleted)
        .Sum(i => i.SizeBytes);

    public bool WasCancelled { get; init; }

    public Enums.CleanupExecutionStatus FinalStatus => WasCancelled
        ? Enums.CleanupExecutionStatus.Cancelled
        : FailedItemCount > 0
            ? Enums.CleanupExecutionStatus.CompletedWithFailures
            : Enums.CleanupExecutionStatus.Completed;

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required DateTimeOffset CompletedAtUtc { get; init; }
}
