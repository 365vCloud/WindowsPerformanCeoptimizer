using WPO.Domain.Models;

namespace WPO.Core.Audit;

/// <summary>
/// Records audit trail entries in memory for the lifetime of the process.
/// Any path-bearing field is masked before storage, never the raw path.
/// </summary>
public interface IAuditLogger
{
    void Log(AuditLogEntry entry);

    IReadOnlyList<AuditLogEntry> GetEntries();
}
