using WPO.Core.Cleanup;
using WPO.Core.Security;

namespace WPO.Core.Tests.Cleanup;

public sealed class SafeTemporaryFileScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"wpo-scanner-tests-{Guid.NewGuid():N}");

    public SafeTemporaryFileScannerTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task ScanAsync_FiltersRecentFilesAndReportsLowRiskMetadata()
    {
        var oldFile = CreateFile("old.tmp", 12, DateTime.UtcNow.AddHours(-25));
        CreateFile("recent.tmp", 20, DateTime.UtcNow.AddHours(-1));

        var result = await CreateScanner().ScanAsync(CancellationToken.None);

        var item = Assert.Single(result);
        Assert.Equal(oldFile, item.FullPath);
        Assert.Equal(12, item.SizeBytes);
        Assert.Equal(WPO.Domain.Enums.RiskLevel.Low, item.RiskLevel);
        Assert.Equal(WPO.Domain.Enums.CleanupCategory.TemporaryFiles, item.Category);
        Assert.Equal("当前用户 Temp 目录中的过期临时文件（仅预览）", item.DetectedReason);
    }

    [Fact]
    public async Task ScanAsync_SkipsRejectedAndReparsePointCandidates()
    {
        var accepted = CreateFile("accepted.tmp", 5, DateTime.UtcNow.AddDays(-2));
        var rejected = CreateFile("rejected.tmp", 7, DateTime.UtcNow.AddDays(-2));
        var outside = Path.Combine(Path.GetTempPath(), $"wpo-scanner-outside-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(outside, "outside");
        File.SetLastWriteTimeUtc(outside, DateTime.UtcNow.AddDays(-2));

        try
        {
            var link = Path.Combine(_root, "outside-link.tmp");
            File.CreateSymbolicLink(link, outside);
            Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);

            var result = await CreateScanner(path => !path.EndsWith("rejected.tmp", StringComparison.OrdinalIgnoreCase))
                .ScanAsync(CancellationToken.None);

            var item = Assert.Single(result);
            Assert.Equal(accepted, item.FullPath);
            Assert.DoesNotContain(result, item => item.FullPath == rejected || item.FullPath == link);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task ScanAsync_ThrowsWhenCancelled()
    {
        CreateFile("old.tmp", 5, DateTime.UtcNow.AddDays(-2));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateScanner().ScanAsync(cancellation.Token));
    }

    [Fact]
    public async Task ScanAsync_SkipsFailingCandidateAndContinues()
    {
        CreateFile("failure.tmp", 8, DateTime.UtcNow.AddDays(-2));
        var accepted = CreateFile("accepted.tmp", 9, DateTime.UtcNow.AddDays(-2));

        var result = await CreateScanner(path =>
        {
            if (path.EndsWith("failure.tmp", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Simulated per-file validation failure.");
            }

            return true;
        }).ScanAsync(CancellationToken.None);

        var item = Assert.Single(result);
        Assert.Equal(accepted, item.FullPath);
    }

    [Fact]
    public async Task ScanAsync_RespectsCandidateLimitAndCalculatesTotalSize()
    {
        CreateFile("first.tmp", 10, DateTime.UtcNow.AddDays(-2));
        CreateFile("second.tmp", 25, DateTime.UtcNow.AddDays(-2));

        var result = await CreateScanner(maximumCandidateCount: 2).ScanAsync(CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(35, result.Sum(item => item.SizeBytes));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private SafeTemporaryFileScanner CreateScanner(
        Func<string, bool>? allowPath = null,
        int maximumCandidateCount = 100)
    {
        return new SafeTemporaryFileScanner(
            new FakePathSafetyValidator(allowPath ?? (_ => true)),
            new TemporaryFileScannerOptions
            {
                TempRoot = _root,
                MinimumFileAge = TimeSpan.FromHours(24),
                MaximumCandidateCount = maximumCandidateCount
            });
    }

    private string CreateFile(string name, int size, DateTime lastWriteUtc)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, Enumerable.Repeat((byte)1, size).ToArray());
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return Path.GetFullPath(path);
    }

    private sealed class FakePathSafetyValidator(Func<string, bool> allowPath) : IPathSafetyValidator
    {
        public PathValidationResult Validate(string candidatePath)
        {
            return allowPath(candidatePath)
                ? PathValidationResult.Allow(candidatePath)
                : PathValidationResult.Reject("NotInAllowList");
        }
    }
}
