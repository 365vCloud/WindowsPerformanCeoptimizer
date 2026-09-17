using WPO.Core.Cleanup;
using WPO.Domain.Enums;
using WPO.Domain.Models;

namespace WPO.Core.Tests.Cleanup;

public class CleanupPreviewServiceTests
{
    private sealed class FakeScanner : ICleanupScanner
    {
        private readonly IReadOnlyList<CleanupItem> _items;

        public FakeScanner(CleanupCategory category, IReadOnlyList<CleanupItem> items)
        {
            Category = category;
            _items = items;
        }

        public CleanupCategory Category { get; }

        public Task<IReadOnlyList<CleanupItem>> ScanAsync(CancellationToken cancellationToken) => Task.FromResult(_items);
    }

    [Fact]
    public async Task BuildPreviewAsync_FiltersOutItemsRejectedByPathSafetyValidator()
    {
        var allowedItem = new CleanupItem
        {
            FullPath = @"C:\Temp\WpoAllowed\a.tmp",
            Category = CleanupCategory.TemporaryFiles,
            RiskLevel = RiskLevel.Low,
            SizeBytes = 10
        };
        var disallowedItem = new CleanupItem
        {
            FullPath = @"C:\Windows\System32\evil.dll",
            Category = CleanupCategory.TemporaryFiles,
            RiskLevel = RiskLevel.High,
            SizeBytes = 999
        };

        var scanner = new FakeScanner(CleanupCategory.TemporaryFiles, new[] { allowedItem, disallowedItem });
        var validator = new WPO.Core.Security.PathSafetyValidator(new WPO.Core.Security.PathSafetyOptions
        {
            AllowedRoots = { @"C:\Temp\WpoAllowed" }
        });
        var logger = new WPO.Core.Audit.AuditLogger(new WPO.Core.Audit.AuditLogMasker());
        var service = new CleanupPreviewService(new[] { scanner }, validator, logger);

        var result = await service.BuildPreviewAsync(CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(allowedItem.FullPath, result.Items[0].FullPath);
        Assert.Equal(10, result.TotalSizeBytes);
    }
}
