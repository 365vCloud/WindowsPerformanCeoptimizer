namespace WPO.Core.Security;

/// <summary>
/// Outcome of validating a candidate filesystem path against the configured
/// allow/deny policy. When <see cref="IsAllowed"/> is false the path must not
/// be scanned, previewed, or deleted.
/// </summary>
public sealed record PathValidationResult
{
    public required bool IsAllowed { get; init; }

    /// <summary>Fully normalized (and, if applicable, symlink-resolved) absolute path.</summary>
    public string? NormalizedPath { get; init; }

    /// <summary>Machine-readable reason code populated when <see cref="IsAllowed"/> is false.</summary>
    public string? RejectionReason { get; init; }

    public static PathValidationResult Allow(string normalizedPath) => new()
    {
        IsAllowed = true,
        NormalizedPath = normalizedPath
    };

    public static PathValidationResult Reject(string reason, string? normalizedPath = null) => new()
    {
        IsAllowed = false,
        RejectionReason = reason,
        NormalizedPath = normalizedPath
    };
}
