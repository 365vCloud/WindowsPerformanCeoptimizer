namespace WPO.Domain.Enums;

/// <summary>
/// Discrete audit event kinds recorded by <see cref="WPO.Core.Audit.IAuditLogger"/>.
/// </summary>
public enum AuditActionType
{
    ScanStarted,
    PreviewGenerated,
    ExecutionStarted,
    ItemDeleted,
    ItemFailed,
    ExecutionCancelled,
    ExecutionCompleted,
    ExportGenerated
}
