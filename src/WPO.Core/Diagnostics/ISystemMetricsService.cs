using WPO.Domain.Models;

namespace WPO.Core.Diagnostics;

/// <summary>Provides read-only machine metrics without requiring elevation.</summary>
public interface ISystemMetricsService
{
    Task<SystemMetricsSnapshot> GetMetricsAsync(CancellationToken cancellationToken);
}
