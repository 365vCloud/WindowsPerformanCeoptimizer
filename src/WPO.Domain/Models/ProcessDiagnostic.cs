namespace WPO.Domain.Models;

/// <summary>
/// Read-only diagnostic information for one process. Optional metadata is null
/// when Windows does not permit or cannot reliably provide it.
/// </summary>
public sealed record ProcessDiagnostic
{
    public string? Name { get; init; }

    public required int ProcessId { get; init; }

    public required MetricValue<long> MemoryBytes { get; init; }

    public required MetricValue<double> CpuUsagePercent { get; init; }

    public string? ExecutablePath { get; init; }

    public string? Publisher { get; init; }

    public bool? IsSigned { get; init; }
}
