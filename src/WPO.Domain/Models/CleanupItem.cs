using WPO.Domain.Enums;

namespace WPO.Domain.Models;

/// <summary>
/// A single file/folder-level cleanup candidate discovered during a scan.
/// Instances are immutable snapshots; status transitions are represented by
/// creating new instances (see <see cref="WithStatus"/>) so preview results
/// stay safe to share across threads.
/// </summary>
public sealed record CleanupItem
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Fully-qualified, normalized filesystem path of the candidate.</summary>
    public required string FullPath { get; init; }

    public required CleanupCategory Category { get; init; }

    public required RiskLevel RiskLevel { get; init; }

    public required long SizeBytes { get; init; }

    public CleanupItemStatus Status { get; init; } = CleanupItemStatus.Pending;

    public DateTimeOffset? LastModifiedUtc { get; init; }

    /// <summary>Human-readable reason the scanner flagged this path (for UI/audit display).</summary>
    public string? DetectedReason { get; init; }

    public CleanupItem WithStatus(CleanupItemStatus status) => this with { Status = status };
}
