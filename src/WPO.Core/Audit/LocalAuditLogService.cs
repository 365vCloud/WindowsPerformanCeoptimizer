using System.Text;
using System.Text.Json;
using WPO.Domain.Models;

namespace WPO.Core.Audit;

/// <summary>
/// Stores sanitized audit records as UTF-8 JSON Lines under the current user's
/// LocalAppData. It never records file contents or raw path values.
/// </summary>
public sealed class LocalAuditLogService : IAuditLogService
{
    private const string ApplicationDirectoryName = "WindowsPerformanceOptimizer";
    private const string LogFileName = "audit.jsonl";
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly IAuditLogMasker _masker;
    private readonly string _logPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<AuditLogEntry> _entries = [];

    public LocalAuditLogService(IAuditLogMasker masker)
        : this(masker, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationDirectoryName,
            LogFileName))
    {
    }

    public LocalAuditLogService(IAuditLogMasker masker, string logPath)
    {
        _masker = masker ?? throw new ArgumentNullException(nameof(masker));
        _logPath = string.IsNullOrWhiteSpace(logPath) ? throw new ArgumentException("A log path is required.", nameof(logPath)) : logPath;
    }

    public void Log(AuditLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var sanitized = AuditLogSanitizer.Sanitize(entry, _masker);
        _gate.Wait();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath) ?? throw new InvalidOperationException("Audit log directory is unavailable."));
            File.AppendAllText(_logPath, JsonSerializer.Serialize(sanitized) + Environment.NewLine, Utf8WithoutBom);
            _entries.Add(sanitized);
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<AuditLogEntry> GetEntries()
    {
        _gate.Wait();
        try
        {
            return _entries.ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AuditLogEntry>> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_logPath))
            {
                return Array.Empty<AuditLogEntry>();
            }

            var entries = new List<AuditLogEntry>();
            using var reader = new StreamReader(_logPath, Utf8WithoutBom, detectEncodingFromByteOrderMarks: true);
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { Length: > 0 } line)
            {
                entries.Add(JsonSerializer.Deserialize<AuditLogEntry>(line)
                    ?? throw new InvalidDataException("Audit log contains an empty record."));
            }

            return entries;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(bool confirmed, CancellationToken cancellationToken = default)
    {
        if (!confirmed)
        {
            throw new InvalidOperationException("Audit log clearing requires explicit confirmation.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_logPath))
            {
                File.Delete(_logPath);
            }

            _entries.Clear();
        }
        finally
        {
            _gate.Release();
        }
    }

}
