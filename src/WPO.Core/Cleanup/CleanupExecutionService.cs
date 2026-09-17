using WPO.Core.Audit;
using WPO.Core.RecycleBin;
using WPO.Core.Security;
using WPO.Domain.Enums;
using WPO.Domain.Models;

namespace WPO.Core.Cleanup;

/// <summary>
/// Default execution engine. Safety invariants enforced here:
/// <list type="bullet">
/// <item>Permanent deletion is only ever attempted when the selection explicitly
/// requests <see cref="DeletionMode.PermanentWithConfirmation"/> AND
/// <see cref="CleanupSelection.ConfirmPermanentDeletion"/> is true; otherwise an
/// <see cref="InvalidOperationException"/> is thrown before anything is touched.</item>
/// <item>Medium/High risk items are skipped unless their matching confirmation
/// flag is set, even if the item id is present in the selection.</item>
/// <item>Every path is re-validated against <see cref="IPathSafetyValidator"/> right
/// before deletion, regardless of earlier preview-time validation.</item>
/// <item>Cancellation stops further deletions immediately; already-processed items
/// keep their real outcome, unprocessed items are marked Cancelled.</item>
/// <item><see cref="CleanupExecutionResult.TotalBytesFreed"/> is computed purely from
/// items that actually report <see cref="CleanupItemStatus.Deleted"/>.</item>
/// </list>
/// </summary>
public sealed class CleanupExecutionService : ICleanupExecutionService
{
    private readonly IRecycleBinService _recycleBinService;
    private readonly IPathSafetyValidator _pathSafetyValidator;
    private readonly IAuditLogger _auditLogger;

    public CleanupExecutionService(
        IRecycleBinService recycleBinService,
        IPathSafetyValidator pathSafetyValidator,
        IAuditLogger auditLogger)
    {
        _recycleBinService = recycleBinService;
        _pathSafetyValidator = pathSafetyValidator;
        _auditLogger = auditLogger;
    }

    public async Task<CleanupExecutionResult> ExecuteAsync(
        CleanupPreviewResult preview,
        CleanupSelection selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(selection);

        if (selection.DeletionMode == DeletionMode.PermanentWithConfirmation && !selection.ConfirmPermanentDeletion)
        {
            throw new InvalidOperationException(
                "Permanent deletion was requested without an explicit second confirmation. " +
                "Refusing to execute; the system never permanently deletes automatically.");
        }

        var correlationId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;

        _auditLogger.Log(new AuditLogEntry
        {
            TimestampUtc = startedAt,
            ActionType = AuditActionType.ExecutionStarted,
            Message = $"Execution started for {selection.SelectedItemIds.Count} selected item(s), mode={selection.DeletionMode}.",
            CorrelationId = correlationId
        });

        var results = new List<CleanupExecutionItemResult>();
        var cancelledFromHereOn = false;

        foreach (var item in preview.Items)
        {
            if (!selection.SelectedItemIds.Contains(item.Id))
            {
                results.Add(ToResult(item, false, CleanupItemStatus.Skipped, CleanupExecutionReason.NotSelected, "Not selected."));
                continue;
            }

            if (cancelledFromHereOn || cancellationToken.IsCancellationRequested)
            {
                cancelledFromHereOn = true;
                results.Add(ToResult(item, true, CleanupItemStatus.Cancelled, CleanupExecutionReason.CancelledBeforeProcessing, "Execution cancelled before this item was processed."));
                continue;
            }

            if (item.RiskLevel == RiskLevel.Medium && !selection.ConfirmMediumRisk)
            {
                results.Add(ToResult(item, true, CleanupItemStatus.Skipped, CleanupExecutionReason.RequiresMediumRiskConfirmation, "Medium risk item requires explicit confirmation."));
                continue;
            }

            if (item.RiskLevel == RiskLevel.High && !selection.ConfirmHighRisk)
            {
                results.Add(ToResult(item, true, CleanupItemStatus.Skipped, CleanupExecutionReason.RequiresHighRiskConfirmation, "High risk item requires explicit confirmation."));
                continue;
            }

            var validation = _pathSafetyValidator.Validate(item.FullPath);
            if (!validation.IsAllowed)
            {
                var failure = ToResult(item, true, CleanupItemStatus.Failed, CleanupExecutionReason.PathSafetyValidationFailed, $"Path failed safety validation: {validation.RejectionReason}.");
                results.Add(failure);
                LogItemFailure(item, correlationId, failure.ErrorMessage!);
                continue;
            }

            try
            {
                var operationResult = selection.DeletionMode == DeletionMode.PermanentWithConfirmation
                    ? await _recycleBinService.DeletePermanentlyAsync(validation.NormalizedPath!, cancellationToken).ConfigureAwait(false)
                    : await _recycleBinService.MoveToRecycleBinAsync(validation.NormalizedPath!, cancellationToken).ConfigureAwait(false);

                if (operationResult.Succeeded)
                {
                    var success = ToResult(item, true, CleanupItemStatus.Deleted, CleanupExecutionReason.None, errorMessage: null);
                    results.Add(success);
                    LogItemDeleted(item, correlationId);
                }
                else
                {
                    var failure = ToResult(item, true, CleanupItemStatus.Failed, CleanupExecutionReason.RecycleBinOperationFailed, operationResult.ErrorMessage);
                    results.Add(failure);
                    LogItemFailure(item, correlationId, operationResult.ErrorMessage ?? "Unknown error.");
                }
            }
            catch (OperationCanceledException)
            {
                cancelledFromHereOn = true;
                results.Add(ToResult(item, true, CleanupItemStatus.Cancelled, CleanupExecutionReason.CancelledDuringProcessing, "Execution cancelled while processing this item."));
            }
        }

        var completedAt = DateTimeOffset.UtcNow;
        var wasCancelled = cancelledFromHereOn;

        var result = new CleanupExecutionResult
        {
            Items = results,
            WasCancelled = wasCancelled,
            StartedAtUtc = startedAt,
            CompletedAtUtc = completedAt
        };

        _auditLogger.Log(new AuditLogEntry
        {
            TimestampUtc = completedAt,
            ActionType = wasCancelled ? AuditActionType.ExecutionCancelled : AuditActionType.ExecutionCompleted,
            Message = $"Execution finished. Deleted={results.Count(r => r.Status == CleanupItemStatus.Deleted)}, " +
                      $"Failed={results.Count(r => r.Status == CleanupItemStatus.Failed)}, " +
                      $"Skipped={results.Count(r => r.Status == CleanupItemStatus.Skipped)}, " +
                      $"Cancelled={results.Count(r => r.Status == CleanupItemStatus.Cancelled)}, " +
                      $"BytesFreed={result.TotalBytesFreed}.",
            CorrelationId = correlationId
        });

        return result;
    }

    private static CleanupExecutionItemResult ToResult(CleanupItem item, bool wasSelected, CleanupItemStatus status, CleanupExecutionReason reason, string? errorMessage) => new()
    {
        ItemId = item.Id,
        FullPath = item.FullPath,
        Status = status,
        SizeBytes = item.SizeBytes,
        WasSelected = wasSelected,
        Reason = reason,
        ErrorMessage = errorMessage
    };

    private void LogItemDeleted(CleanupItem item, Guid correlationId) => _auditLogger.Log(new AuditLogEntry
    {
        TimestampUtc = DateTimeOffset.UtcNow,
        ActionType = AuditActionType.ItemDeleted,
        Message = "Item deleted.",
        Category = item.Category,
        RiskLevel = item.RiskLevel,
        MaskedPath = item.FullPath,
        SizeBytes = item.SizeBytes,
        CorrelationId = correlationId
    });

    private void LogItemFailure(CleanupItem item, Guid correlationId, string errorMessage) => _auditLogger.Log(new AuditLogEntry
    {
        TimestampUtc = DateTimeOffset.UtcNow,
        ActionType = AuditActionType.ItemFailed,
        Message = $"Item deletion failed: {errorMessage}",
        Category = item.Category,
        RiskLevel = item.RiskLevel,
        MaskedPath = item.FullPath,
        SizeBytes = item.SizeBytes,
        CorrelationId = correlationId
    });
}
