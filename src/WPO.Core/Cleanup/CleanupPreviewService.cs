using WPO.Core.Audit;
using WPO.Domain.Enums;
using WPO.Domain.Models;

namespace WPO.Core.Cleanup;

/// <summary>
/// Aggregates the output of every registered <see cref="ICleanupScanner"/> into a
/// single <see cref="CleanupPreviewResult"/>. Paths are re-validated defensively
/// even though scanners are expected to have already filtered them.
/// </summary>
public sealed class CleanupPreviewService : ICleanupPreviewService
{
    private readonly IReadOnlyList<ICleanupScanner> _scanners;
    private readonly Security.IPathSafetyValidator _pathSafetyValidator;
    private readonly IAuditLogger _auditLogger;

    public CleanupPreviewService(
        IEnumerable<ICleanupScanner> scanners,
        Security.IPathSafetyValidator pathSafetyValidator,
        IAuditLogger auditLogger)
    {
        _scanners = scanners.ToList();
        _pathSafetyValidator = pathSafetyValidator;
        _auditLogger = auditLogger;
    }

    public async Task<CleanupPreviewResult> BuildPreviewAsync(CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();
        _auditLogger.Log(new AuditLogEntry
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            ActionType = AuditActionType.ScanStarted,
            Message = $"Scan started across {_scanners.Count} scanner(s).",
            CorrelationId = correlationId
        });

        var items = new List<CleanupItem>();

        foreach (var scanner in _scanners)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var found = await scanner.ScanAsync(cancellationToken).ConfigureAwait(false);

            foreach (var item in found)
            {
                var validation = _pathSafetyValidator.Validate(item.FullPath);
                if (validation.IsAllowed)
                {
                    items.Add(item);
                }
            }
        }

        var result = new CleanupPreviewResult
        {
            Items = items,
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };

        _auditLogger.Log(new AuditLogEntry
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            ActionType = AuditActionType.PreviewGenerated,
            Message = $"Preview generated with {result.TotalItemCount} item(s), {result.TotalSizeBytes} byte(s).",
            CorrelationId = correlationId
        });

        return result;
    }
}
