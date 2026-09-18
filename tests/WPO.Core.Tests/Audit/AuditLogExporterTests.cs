using WPO.Core.Audit;
using WPO.Domain.Enums;
using WPO.Domain.Models;

namespace WPO.Core.Tests.Audit;

public class AuditLogExporterTests
{
    private static AuditLogEntry MakeEntry(string message = "Test message", string? maskedPath = @"<user>\Temp\file.tmp") => new()
    {
        TimestampUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        ActionType = AuditActionType.ItemDeleted,
        Message = message,
        Category = CleanupCategory.TemporaryFiles,
        RiskLevel = RiskLevel.Low,
        MaskedPath = maskedPath,
        SizeBytes = 123,
        CorrelationId = Guid.NewGuid()
    };

    [Fact]
    public void ExportToCsv_ProducesHeaderAndOneRowPerEntry()
    {
        var exporter = new AuditLogExporter();
        var entries = new[] { MakeEntry(), MakeEntry("Second") };

        var csv = exporter.ExportToCsv(entries);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length); // header + 2 rows
        Assert.StartsWith("TimestampUtc,ActionType,Category,RiskLevel,MaskedPath,SizeBytes,CorrelationId,Message", lines[0]);
    }

    [Fact]
    public void ExportToCsv_EscapesFieldsContainingCommas()
    {
        var exporter = new AuditLogExporter();
        var entries = new[] { MakeEntry("Message, with a comma") };

        var csv = exporter.ExportToCsv(entries);

        Assert.Contains("\"Message, with a comma\"", csv);
    }

    [Fact]
    public void ExportToJson_RoundTripsEntryCount()
    {
        var exporter = new AuditLogExporter();
        var entries = new[] { MakeEntry(), MakeEntry("Second") };

        var json = exporter.ExportToJson(entries);
        var parsed = System.Text.Json.JsonSerializer.Deserialize<AuditLogEntry[]>(json);

        Assert.NotNull(parsed);
        Assert.Equal(2, parsed!.Length);
    }

    [Fact]
    public void ExportToCsv_NeverContainsRawUnmaskedUserProfilePath()
    {
        var exporter = new AuditLogExporter();
        var entries = new[] { MakeEntry(maskedPath: @"<user>\AppData\Local\Temp\a.tmp") };

        var csv = exporter.ExportToCsv(entries);

        Assert.DoesNotContain(Environment.UserName, csv);
    }
}
