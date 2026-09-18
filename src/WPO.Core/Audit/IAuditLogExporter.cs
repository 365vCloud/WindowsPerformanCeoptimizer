using WPO.Domain.Models;

namespace WPO.Core.Audit;

/// <summary>
/// Serializes audit log entries for offline review. Entries passed in are
/// expected to already have masked paths (as produced by <see cref="IAuditLogger"/>);
/// this exporter does not perform any additional masking itself.
/// </summary>
public interface IAuditLogExporter
{
    string ExportToCsv(IEnumerable<AuditLogEntry> entries);

    string ExportToJson(IEnumerable<AuditLogEntry> entries);
}
