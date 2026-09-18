using System.Text;
using WPO.Core.Export;
using WPO.Domain.Enums;
using WPO.Domain.Models;

namespace WPO.Core.Tests.Export;

public sealed class CleanupResultExportServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"wpo-export-tests-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(ExportFormat.Csv, "result.csv")]
    [InlineData(ExportFormat.Json, "result.json")]
    public async Task ExportCleanupResultAsync_WritesUtf8WithoutBom(ExportFormat format, string fileName)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, fileName);

        await new CleanupResultExportService().ExportCleanupResultAsync(CreateResult(), path, format);

        var bytes = await File.ReadAllBytesAsync(path);
        Assert.DoesNotContain((byte)0xEF, bytes.Take(3));
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains(format == ExportFormat.Csv ? "ActualBytesFreed" : "\"Items\"", text);
    }

    [Fact]
    public async Task ExportCleanupResultAsync_FailureDoesNotChangeReport()
    {
        var result = CreateResult();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            new CleanupResultExportService().ExportCleanupResultAsync(result, "\0", ExportFormat.Json));

        Assert.Equal(100, result.EstimatedBytes);
        Assert.Equal(100, result.TotalBytesFreed);
        Assert.Equal(CleanupExecutionStatus.Completed, result.FinalStatus);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static CleanupExecutionResult CreateResult() => new()
    {
        Items =
        [
            new CleanupExecutionItemResult
            {
                ItemId = Guid.NewGuid(),
                FullPath = @"C:\Temp\test.tmp",
                WasSelected = true,
                Status = CleanupItemStatus.Deleted,
                Reason = CleanupExecutionReason.None,
                SizeBytes = 100
            }
        ],
        StartedAtUtc = DateTimeOffset.UtcNow,
        CompletedAtUtc = DateTimeOffset.UtcNow
    };
}
