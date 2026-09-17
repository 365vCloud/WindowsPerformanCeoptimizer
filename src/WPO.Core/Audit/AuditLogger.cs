using System.Collections.Concurrent;
using WPO.Domain.Models;

namespace WPO.Core.Audit;

/// <summary>
/// Thread-safe in-memory audit logger. Every entry's <see cref="AuditLogEntry.MaskedPath"/>
/// is passed through <see cref="IAuditLogMasker"/> before being stored, so callers may
/// freely pass a raw filesystem path in and never have it leak unmasked.
/// </summary>
public sealed class AuditLogger : IAuditLogger
{
    private readonly IAuditLogMasker _masker;
    private readonly ConcurrentQueue<AuditLogEntry> _entries = new();

    public AuditLogger(IAuditLogMasker masker)
    {
        _masker = masker;
    }

    public void Log(AuditLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var maskedEntry = entry.MaskedPath is null
            ? entry
            : entry with { MaskedPath = _masker.Mask(entry.MaskedPath) };

        _entries.Enqueue(maskedEntry);
    }

    public IReadOnlyList<AuditLogEntry> GetEntries() => _entries.ToArray();
}
