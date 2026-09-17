namespace WPO.Domain.Models;

/// <summary>
/// Aggregate result of a cleanup execution run. <see cref="TotalBytesFreed"/> is
/// computed strictly from items that actually report a Deleted status, never
/// from the originally-selected/candidate size.
/// </summary>
public sealed record CleanupExecutionResult
{
    public required IReadOnlyList<CleanupExecutionItemResult> Items { get; init; }

    public long TotalBytesFreed => Items
        .Where(i => i.Status == Enums.CleanupItemStatus.Deleted)
        .Sum(i => i.SizeBytes);

    public bool WasCancelled { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required DateTimeOffset CompletedAtUtc { get; init; }
}
