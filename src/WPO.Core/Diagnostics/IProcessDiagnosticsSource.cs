using WPO.Domain.Models;

namespace WPO.Core.Diagnostics;

/// <summary>Low-level process snapshot source used by the bounded scan service.</summary>
public interface IProcessDiagnosticsSource
{
    IReadOnlyList<int> GetProcessIds();

    Task<ProcessDiagnostic?> InspectAsync(int processId, CancellationToken cancellationToken);
}
