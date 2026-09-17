using WPO.Domain.Models;

namespace WPO.Core.Diagnostics;

/// <summary>Scans a bounded list of processes without changing process state.</summary>
public interface IPerformanceScanService
{
    Task<PerformanceScanResult> ScanAsync(int maximumProcesses, CancellationToken cancellationToken);
}
