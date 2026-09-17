namespace WPO.Domain.Enums;

/// <summary>Explains why a cleanup item did not complete successfully.</summary>
public enum CleanupExecutionReason
{
    None,
    NotSelected,
    RequiresMediumRiskConfirmation,
    RequiresHighRiskConfirmation,
    PathSafetyValidationFailed,
    RecycleBinOperationFailed,
    CancelledBeforeProcessing,
    CancelledDuringProcessing
}
