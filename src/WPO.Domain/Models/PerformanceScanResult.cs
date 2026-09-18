namespace WPO.Domain.Models;

/// <summary>
/// Bounded, read-only process scan result.
/// </summary>
public sealed record PerformanceScanResult
{
    public required IReadOnlyList<ProcessDiagnostic> Processes { get; init; }

    public required DateTimeOffset CollectedAtUtc { get; init; }
}
