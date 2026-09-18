namespace WPO.Domain.Enums;

/// <summary>Final, derived status of an entire cleanup execution.</summary>
public enum CleanupExecutionStatus
{
    Completed,
    CompletedWithFailures,
    Cancelled
}
