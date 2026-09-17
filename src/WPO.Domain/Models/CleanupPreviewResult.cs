namespace WPO.Domain.Models;

/// <summary>
/// Result of a non-destructive scan/preview pass. Never mutates or deletes
/// anything on disk; it is purely informational for the user to review.
/// </summary>
public sealed record CleanupPreviewResult
{
    public required IReadOnlyList<CleanupItem> Items { get; init; }

    public long TotalSizeBytes => Items.Sum(i => i.SizeBytes);

    public int TotalItemCount => Items.Count;

    public required DateTimeOffset GeneratedAtUtc { get; init; }
}
