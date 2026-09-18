using WPO.Domain.Models;

namespace WPO.Core.Diagnostics;

/// <summary>Collects isolated process snapshots, then sorts and bounds the result.</summary>
public sealed class PerformanceScanService : IPerformanceScanService
{
    private readonly IProcessDiagnosticsSource _source;

    public PerformanceScanService(IProcessDiagnosticsSource source)
    {
        _source = source;
    }

    public async Task<PerformanceScanResult> ScanAsync(int maximumProcesses, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumProcesses);
        cancellationToken.ThrowIfCancellationRequested();

        var processIds = _source.GetProcessIds();
        var inspections = processIds.Select(id => InspectIsolatedAsync(id, cancellationToken));
        var diagnostics = (await Task.WhenAll(inspections).ConfigureAwait(false))
            .Where(diagnostic => diagnostic is not null)
            .Select(diagnostic => diagnostic!)
            .OrderByDescending(diagnostic => diagnostic.MemoryBytes.Value ?? long.MinValue)
            .ThenBy(diagnostic => diagnostic.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maximumProcesses)
            .ToArray();

        return new PerformanceScanResult
        {
            Processes = diagnostics,
            CollectedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private async Task<ProcessDiagnostic?> InspectIsolatedAsync(int processId, CancellationToken cancellationToken)
    {
        try
        {
            return await _source.InspectAsync(processId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
