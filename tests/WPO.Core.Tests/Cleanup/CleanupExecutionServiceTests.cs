using WPO.Core.Audit;
using WPO.Core.Security;
using WPO.Core.Cleanup;
using WPO.Core.Tests.TestSupport;
using WPO.Domain.Enums;
using WPO.Domain.Models;

namespace WPO.Core.Tests.Cleanup;

public class CleanupExecutionServiceTests
{
    private static CleanupItem MakeItem(RiskLevel risk = RiskLevel.Low, long sizeBytes = 100, string? path = null) => new()
    {
        FullPath = path ?? $@"C:\Temp\{Guid.NewGuid():N}.tmp",
        Category = CleanupCategory.TemporaryFiles,
        RiskLevel = risk,
        SizeBytes = sizeBytes
    };

    private static CleanupExecutionService CreateService(FakeRecycleBinService recycleBin, out IAuditLogger logger)
    {
        logger = new AuditLogger(new AuditLogMasker());
        return new CleanupExecutionService(recycleBin, new AlwaysAllowPathSafetyValidator(), logger);
    }

    [Fact]
    public async Task ExecuteAsync_OnlySelectedItems_AreDeleted_OthersAreSkipped()
    {
        var selectedItem = MakeItem();
        var unselectedItem = MakeItem();
        var preview = new CleanupPreviewResult { Items = new[] { selectedItem, unselectedItem }, GeneratedAtUtc = DateTimeOffset.UtcNow };
        var recycleBin = new FakeRecycleBinService();
        var service = CreateService(recycleBin, out _);

        var selection = new CleanupSelection { SelectedItemIds = new HashSet<Guid> { selectedItem.Id } };

        var result = await service.ExecuteAsync(preview, selection, CancellationToken.None);

        var selectedResult = result.Items.Single(i => i.ItemId == selectedItem.Id);
        var unselectedResult = result.Items.Single(i => i.ItemId == unselectedItem.Id);

        Assert.Equal(CleanupItemStatus.Deleted, selectedResult.Status);
        Assert.Equal(CleanupItemStatus.Skipped, unselectedResult.Status);
        Assert.Contains(selectedItem.FullPath, recycleBin.MovedToRecycleBin);
        Assert.DoesNotContain(unselectedItem.FullPath, recycleBin.MovedToRecycleBin);
    }

    [Fact]
    public async Task ExecuteAsync_MediumRiskItem_WithoutConfirmation_IsSkipped()
    {
        var item = MakeItem(RiskLevel.Medium);
        var preview = new CleanupPreviewResult { Items = new[] { item }, GeneratedAtUtc = DateTimeOffset.UtcNow };
        var recycleBin = new FakeRecycleBinService();
        var service = CreateService(recycleBin, out _);

        var selection = new CleanupSelection
        {
            SelectedItemIds = new HashSet<Guid> { item.Id },
            ConfirmMediumRisk = false
        };

        var result = await service.ExecuteAsync(preview, selection, CancellationToken.None);

        Assert.Equal(CleanupItemStatus.Skipped, result.Items.Single().Status);
        Assert.Empty(recycleBin.MovedToRecycleBin);
    }

    [Fact]
    public async Task ExecuteAsync_MediumRiskItem_WithConfirmation_IsDeleted()
    {
        var item = MakeItem(RiskLevel.Medium);
        var preview = new CleanupPreviewResult { Items = new[] { item }, GeneratedAtUtc = DateTimeOffset.UtcNow };
        var recycleBin = new FakeRecycleBinService();
        var service = CreateService(recycleBin, out _);

        var selection = new CleanupSelection
        {
            SelectedItemIds = new HashSet<Guid> { item.Id },
            ConfirmMediumRisk = true
        };

        var result = await service.ExecuteAsync(preview, selection, CancellationToken.None);

        Assert.Equal(CleanupItemStatus.Deleted, result.Items.Single().Status);
        Assert.Contains(item.FullPath, recycleBin.MovedToRecycleBin);
    }

    [Fact]
    public async Task ExecuteAsync_HighRiskItem_WithoutConfirmation_IsSkipped()
    {
        var item = MakeItem(RiskLevel.High);
        var preview = new CleanupPreviewResult { Items = new[] { item }, GeneratedAtUtc = DateTimeOffset.UtcNow };
        var recycleBin = new FakeRecycleBinService();
        var service = CreateService(recycleBin, out _);

        var selection = new CleanupSelection
        {
            SelectedItemIds = new HashSet<Guid> { item.Id },
            ConfirmHighRisk = false
        };

        var result = await service.ExecuteAsync(preview, selection, CancellationToken.None);

        Assert.Equal(CleanupItemStatus.Skipped, result.Items.Single().Status);
    }

    [Fact]
    public async Task ExecuteAsync_PermanentDeletion_WithoutSecondConfirmation_ThrowsAndDeletesNothing()
    {
        var item = MakeItem();
        var preview = new CleanupPreviewResult { Items = new[] { item }, GeneratedAtUtc = DateTimeOffset.UtcNow };
        var recycleBin = new FakeRecycleBinService();
        var service = CreateService(recycleBin, out _);

        var selection = new CleanupSelection
        {
            SelectedItemIds = new HashSet<Guid> { item.Id },
            DeletionMode = DeletionMode.PermanentWithConfirmation,
            ConfirmPermanentDeletion = false
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExecuteAsync(preview, selection, CancellationToken.None));

        Assert.Empty(recycleBin.PermanentlyDeleted);
        Assert.Empty(recycleBin.MovedToRecycleBin);
    }

    [Fact]
    public async Task ExecuteAsync_PermanentDeletion_WithSecondConfirmation_DeletesPermanently()
    {
        var item = MakeItem();
        var preview = new CleanupPreviewResult { Items = new[] { item }, GeneratedAtUtc = DateTimeOffset.UtcNow };
        var recycleBin = new FakeRecycleBinService();
        var service = CreateService(recycleBin, out _);

        var selection = new CleanupSelection
        {
            SelectedItemIds = new HashSet<Guid> { item.Id },
            DeletionMode = DeletionMode.PermanentWithConfirmation,
            ConfirmPermanentDeletion = true
        };

        var result = await service.ExecuteAsync(preview, selection, CancellationToken.None);

        Assert.Equal(CleanupItemStatus.Deleted, result.Items.Single().Status);
        Assert.Contains(item.FullPath, recycleBin.PermanentlyDeleted);
        Assert.Empty(recycleBin.MovedToRecycleBin);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationMidRun_StopsFurtherDeletions_MarksRemainingCancelled()
    {
        var first = MakeItem();
        var second = MakeItem();
        var third = MakeItem();
        var preview = new CleanupPreviewResult { Items = new[] { first, second, third }, GeneratedAtUtc = DateTimeOffset.UtcNow };
        var recycleBin = new FakeRecycleBinService();
        using var cts = new CancellationTokenSource();

        // Cancel externally as soon as the first item begins processing, before the second runs.
        recycleBin.OnMoveInvoked = path =>
        {
            if (path == first.FullPath)
            {
                cts.Cancel();
            }
        };

        var service = CreateService(recycleBin, out _);
        var selection = new CleanupSelection
        {
            SelectedItemIds = new HashSet<Guid> { first.Id, second.Id, third.Id }
        };

        var result = await service.ExecuteAsync(preview, selection, cts.Token);

        Assert.True(result.WasCancelled);
        Assert.Equal(CleanupItemStatus.Deleted, result.Items.Single(i => i.ItemId == first.Id).Status);
        Assert.Equal(CleanupItemStatus.Cancelled, result.Items.Single(i => i.ItemId == second.Id).Status);
        Assert.Equal(CleanupItemStatus.Cancelled, result.Items.Single(i => i.ItemId == third.Id).Status);
        Assert.DoesNotContain(second.FullPath, recycleBin.MovedToRecycleBin);
        Assert.DoesNotContain(third.FullPath, recycleBin.MovedToRecycleBin);
    }

    [Fact]
    public async Task ExecuteAsync_ActualBytesFreed_OnlyCountsSuccessfullyDeletedItems()
    {
        var succeeding = MakeItem(sizeBytes: 1000);
        var failing = MakeItem(sizeBytes: 5000);
        var preview = new CleanupPreviewResult { Items = new[] { succeeding, failing }, GeneratedAtUtc = DateTimeOffset.UtcNow };
        var recycleBin = new FakeRecycleBinService();
        recycleBin.PathsThatFail.Add(failing.FullPath);

        var service = CreateService(recycleBin, out _);
        var selection = new CleanupSelection
        {
            SelectedItemIds = new HashSet<Guid> { succeeding.Id, failing.Id }
        };

        var result = await service.ExecuteAsync(preview, selection, CancellationToken.None);

        Assert.Equal(1000, result.TotalBytesFreed);
        Assert.Equal(CleanupItemStatus.Deleted, result.Items.Single(i => i.ItemId == succeeding.Id).Status);
        Assert.Equal(CleanupItemStatus.Failed, result.Items.Single(i => i.ItemId == failing.Id).Status);
    }

    [Fact]
    public async Task ExecuteAsync_PathFailingSafetyValidation_IsMarkedFailed_NotDeleted()
    {
        var item = MakeItem();
        var preview = new CleanupPreviewResult { Items = new[] { item }, GeneratedAtUtc = DateTimeOffset.UtcNow };
        var recycleBin = new FakeRecycleBinService();
        var logger = new AuditLogger(new AuditLogMasker());
        var rejectingValidator = new RejectingPathSafetyValidator(item.FullPath);
        var service = new CleanupExecutionService(recycleBin, rejectingValidator, logger);

        var selection = new CleanupSelection { SelectedItemIds = new HashSet<Guid> { item.Id } };

        var result = await service.ExecuteAsync(preview, selection, CancellationToken.None);

        Assert.Equal(CleanupItemStatus.Failed, result.Items.Single().Status);
        Assert.Empty(recycleBin.MovedToRecycleBin);
    }

    [Fact]
    public async Task ExecuteAsync_HighRiskItem_WithConfirmation_IsDeleted()
    {
        var item = MakeItem(RiskLevel.High);
        var preview = new CleanupPreviewResult { Items = new[] { item }, GeneratedAtUtc = DateTimeOffset.UtcNow };
        var recycleBin = new FakeRecycleBinService();
        var service = CreateService(recycleBin, out _);

        var selection = new CleanupSelection
        {
            SelectedItemIds = new HashSet<Guid> { item.Id },
            ConfirmHighRisk = true
        };

        var result = await service.ExecuteAsync(preview, selection, CancellationToken.None);

        Assert.Equal(CleanupItemStatus.Deleted, result.Items.Single().Status);
        Assert.Contains(item.FullPath, recycleBin.MovedToRecycleBin);
    }

    [Fact]
    public async Task ExecuteAsync_NothingSelected_EveryItemIsSkipped_NothingDeleted()
    {
        var first = MakeItem();
        var second = MakeItem();
        var preview = new CleanupPreviewResult { Items = new[] { first, second }, GeneratedAtUtc = DateTimeOffset.UtcNow };
        var recycleBin = new FakeRecycleBinService();
        var service = CreateService(recycleBin, out _);

        var selection = new CleanupSelection { SelectedItemIds = new HashSet<Guid>() };

        var result = await service.ExecuteAsync(preview, selection, CancellationToken.None);

        Assert.All(result.Items, i => Assert.Equal(CleanupItemStatus.Skipped, i.Status));
        Assert.Equal(0, result.TotalBytesFreed);
        Assert.Empty(recycleBin.MovedToRecycleBin);
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyCancelledToken_MarksEverySelectedItemCancelled_DeletesNothing()
    {
        var first = MakeItem();
        var second = MakeItem();
        var preview = new CleanupPreviewResult { Items = new[] { first, second }, GeneratedAtUtc = DateTimeOffset.UtcNow };
        var recycleBin = new FakeRecycleBinService();
        var service = CreateService(recycleBin, out _);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var selection = new CleanupSelection { SelectedItemIds = new HashSet<Guid> { first.Id, second.Id } };

        var result = await service.ExecuteAsync(preview, selection, cts.Token);

        Assert.True(result.WasCancelled);
        Assert.All(result.Items, i => Assert.Equal(CleanupItemStatus.Cancelled, i.Status));
        Assert.Empty(recycleBin.MovedToRecycleBin);
    }

    [Fact]
    public async Task ExecuteAsync_ReValidatesRightBeforeDeletion_EvenWhenPreviewOnceConsideredItSafe()
    {
        // Simulates a path that was safe when the preview/scan ran but is no
        // longer allowed by the time execution actually happens (e.g. moved
        // outside the allow-list in between); the execution service must not
        // trust the earlier preview-time validation.
        var item = MakeItem();
        var preview = new CleanupPreviewResult { Items = new[] { item }, GeneratedAtUtc = DateTimeOffset.UtcNow };
        var recycleBin = new FakeRecycleBinService();
        var logger = new AuditLogger(new AuditLogMasker());
        var flakyValidator = new BecameUnsafePathSafetyValidator(item.FullPath);
        var service = new CleanupExecutionService(recycleBin, flakyValidator, logger);

        var selection = new CleanupSelection { SelectedItemIds = new HashSet<Guid> { item.Id } };

        var result = await service.ExecuteAsync(preview, selection, CancellationToken.None);

        Assert.Equal(CleanupItemStatus.Failed, result.Items.Single().Status);
        Assert.Empty(recycleBin.MovedToRecycleBin);
        Assert.Equal(0, result.TotalBytesFreed);
    }

    private sealed class BecameUnsafePathSafetyValidator : IPathSafetyValidator
    {
        private readonly string _watchedPath;

        public BecameUnsafePathSafetyValidator(string watchedPath) => _watchedPath = watchedPath;

        // Always rejects on re-validation, modeling the "no longer safe by
        // execution time" scenario regardless of any earlier preview pass.
        public PathValidationResult Validate(string candidatePath) =>
            candidatePath == _watchedPath
                ? PathValidationResult.Reject("Path is no longer within the allow-list.")
                : PathValidationResult.Allow(candidatePath);
    }

    private sealed class RejectingPathSafetyValidator : IPathSafetyValidator
    {
        private readonly string _pathToReject;

        public RejectingPathSafetyValidator(string pathToReject) => _pathToReject = pathToReject;

        public PathValidationResult Validate(string candidatePath) =>
            candidatePath == _pathToReject
                ? PathValidationResult.Reject("Rejected")
                : PathValidationResult.Allow(candidatePath);
    }
}

