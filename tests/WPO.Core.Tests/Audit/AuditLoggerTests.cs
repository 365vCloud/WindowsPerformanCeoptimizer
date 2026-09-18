using WPO.Core.Audit;
using WPO.Domain.Enums;
using WPO.Domain.Models;

namespace WPO.Core.Tests.Audit;

public class AuditLoggerTests
{
    [Fact]
    public void Log_MasksPathBeforeStoringEntry()
    {
        var logger = new AuditLogger(new AuditLogMasker(@"C:\Users\Bob", "Bob"));

        logger.Log(new AuditLogEntry
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            ActionType = AuditActionType.ItemDeleted,
            Message = "deleted",
            MaskedPath = @"C:\Users\Bob\AppData\Local\Temp\file.tmp",
            CorrelationId = Guid.NewGuid()
        });

        var entry = Assert.Single(logger.GetEntries());
        Assert.Equal(@"<user>\AppData\Local\Temp\file.tmp", entry.MaskedPath);
    }

    [Fact]
    public void Log_EntryWithoutPath_IsStoredUnchanged()
    {
        var logger = new AuditLogger(new AuditLogMasker());

        logger.Log(new AuditLogEntry
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            ActionType = AuditActionType.ScanStarted,
            Message = "scan started",
            CorrelationId = Guid.NewGuid()
        });

        var entry = Assert.Single(logger.GetEntries());
        Assert.Null(entry.MaskedPath);
    }
}
