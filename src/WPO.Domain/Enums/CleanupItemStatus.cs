namespace WPO.Domain.Enums;

/// <summary>
/// Lifecycle status of a single cleanup candidate item.
/// </summary>
public enum CleanupItemStatus
{
    Pending,
    Selected,
    Skipped,
    Deleted,
    Failed,
    Cancelled
}
