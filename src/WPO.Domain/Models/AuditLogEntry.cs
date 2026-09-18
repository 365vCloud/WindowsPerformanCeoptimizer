using WPO.Domain.Enums;

namespace WPO.Domain.Models;

/// <summary>
/// A single audit trail entry. Paths stored here must already be masked by
/// the audit subsystem before this entry is persisted or exported.
/// </summary>
public sealed record AuditLogEntry
{
    public required DateTimeOffset TimestampUtc { get; init; }

    public required AuditActionType ActionType { get; init; }

    public required string Message { get; init; }

    public CleanupCategory? Category { get; init; }

    public RiskLevel? RiskLevel { get; init; }

    /// <summary>Masked/redacted path — user profile segments replaced, never the raw path.</summary>
    public string? MaskedPath { get; init; }

    public long? SizeBytes { get; init; }

    public required Guid CorrelationId { get; init; }
}
