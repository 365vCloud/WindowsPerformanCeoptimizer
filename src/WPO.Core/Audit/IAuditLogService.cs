using WPO.Domain.Models;

namespace WPO.Core.Audit;

/// <summary>Persistent, privacy-preserving audit log service.</summary>
public interface IAuditLogService : IAuditLogger
{
    Task<IReadOnlyList<AuditLogEntry>> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>Clears the local audit log only after an explicit UI confirmation.</summary>
    Task ClearAsync(bool confirmed, CancellationToken cancellationToken = default);
}
