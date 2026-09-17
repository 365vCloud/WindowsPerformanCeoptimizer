using System.Text;
using System.Text.Json;
using WPO.Domain.Models;

namespace WPO.Core.Audit;

/// <summary>
/// Default CSV/JSON exporter for audit log entries.
/// </summary>
public sealed class AuditLogExporter : IAuditLogExporter
{
    private static readonly string[] Header =
    {
        "TimestampUtc", "ActionType", "Category", "RiskLevel", "MaskedPath", "SizeBytes", "CorrelationId", "Message"
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string ExportToCsv(IEnumerable<AuditLogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', Header));

        foreach (var entry in entries)
        {
            var fields = new[]
            {
                entry.TimestampUtc.ToString("o"),
                entry.ActionType.ToString(),
                entry.Category?.ToString() ?? string.Empty,
                entry.RiskLevel?.ToString() ?? string.Empty,
                entry.MaskedPath ?? string.Empty,
                entry.SizeBytes?.ToString() ?? string.Empty,
                entry.CorrelationId.ToString(),
                entry.Message
            };

            builder.AppendLine(string.Join(',', fields.Select(EscapeCsvField)));
        }

        return builder.ToString();
    }

    public string ExportToJson(IEnumerable<AuditLogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return JsonSerializer.Serialize(entries, JsonOptions);
    }

    private static string EscapeCsvField(string field)
    {
        if (field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
        {
            return field;
        }

        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }
}
