namespace WPO.Domain.Models;

/// <summary>
/// Read-only system metrics collected at a single point in time.
/// </summary>
public sealed record SystemMetricsSnapshot
{
    public required MetricValue<double> CpuUsagePercent { get; init; }

    public required MetricValue<long> TotalMemoryBytes { get; init; }

    public required MetricValue<long> UsedMemoryBytes { get; init; }

    public required string SystemDriveName { get; init; }

    public required MetricValue<long> FreeDiskBytes { get; init; }

    public required DateTimeOffset CollectedAtUtc { get; init; }
}
