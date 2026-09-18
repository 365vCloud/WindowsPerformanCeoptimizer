using System.Text;
using WPO.Core.Audit;
using WPO.Domain.Enums;
using WPO.Domain.Models;

namespace WPO.Core.Tests.Audit;

public sealed class LocalAuditLogServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"wpo-tests-{Guid.NewGuid():N}");
    private readonly string _logPath;

    public LocalAuditLogServiceTests()
    {
        _logPath = Path.Combine(_directory, "audit.jsonl");
    }

    [Fact]
    public async Task Log_MasksPathsAndFlattensNewlinesBeforePersisting()
    {
        var service = CreateService();
        service.Log(Entry(@"C:\Users\Bob\AppData\Local\Temp\a.tmp", "failed\r\nat C:\\Users\\Bob\\secret.tmp token=should-not-persist"));

        var entry = Assert.Single(await service.ReadAsync());

        Assert.Equal(@"<user>\AppData\Local\Temp\a.tmp", entry.MaskedPath);
        Assert.Equal("failed  at <path> token=<redacted>", entry.Message);
        var persisted = await File.ReadAllTextAsync(_logPath);
        Assert.DoesNotContain("\r\nat", persisted);
        Assert.DoesNotContain(@"C:\Users\Bob", persisted);
        Assert.DoesNotContain("should-not-persist", persisted);
    }

    [Fact]
    public async Task ClearAsync_WithoutConfirmation_RefusesAndPreservesLog()
    {
        var service = CreateService();
        service.Log(Entry(null, "kept"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ClearAsync(confirmed: false));

        Assert.Single(await service.ReadAsync());
    }

    [Fact]
    public async Task ClearAsync_WithConfirmation_RemovesOnlyTestLog()
    {
        var service = CreateService();
        service.Log(Entry(null, "remove"));

        await service.ClearAsync(confirmed: true);

        Assert.Empty(await service.ReadAsync());
        Assert.False(File.Exists(_logPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private LocalAuditLogService CreateService() => new(new AuditLogMasker(@"C:\Users\Bob", "Bob"), _logPath);

    private static AuditLogEntry Entry(string? path, string message) => new()
    {
        TimestampUtc = DateTimeOffset.UtcNow,
        ActionType = AuditActionType.ItemFailed,
        Message = message,
        MaskedPath = path,
        CorrelationId = Guid.NewGuid()
    };
}
