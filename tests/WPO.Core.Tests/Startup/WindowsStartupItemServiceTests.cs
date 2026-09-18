using WPO.Core.Startup;
using WPO.Domain.Enums;
using WPO.Domain.Models;

namespace WPO.Core.Tests.Startup;

public sealed class WindowsStartupItemServiceTests
{
    [Fact]
    public async Task GetStartupItemsAsync_SortsBySourceThenNameAndLimitsResults()
    {
        var reader = new FakeStartupEntryReader(
            Candidate("Zeta", StartupItemSource.CurrentUserRunRegistry),
            Candidate("Alpha", StartupItemSource.CurrentUserRunRegistry),
            Candidate("EarlyFolderItem", StartupItemSource.CurrentUserStartupFolder),
            Candidate("Beta", StartupItemSource.LocalMachineRunRegistry));
        var service = new WindowsStartupItemService(reader);

        var result = await service.GetStartupItemsAsync(3, CancellationToken.None);

        Assert.Collection(
            result,
            item => Assert.Equal("Alpha", item.Name),
            item => Assert.Equal("Zeta", item.Name),
            item => Assert.Equal("Beta", item.Name));
    }

    [Fact]
    public async Task GetStartupItemsAsync_IsolatesSingleEntryInspectionFailures()
    {
        var reader = new FakeStartupEntryReader(
            Candidate("Healthy", StartupItemSource.CurrentUserRunRegistry),
            Candidate("Fails", StartupItemSource.CurrentUserRunRegistry))
        {
            ThrowForName = "Fails"
        };
        var service = new WindowsStartupItemService(reader);

        var result = await service.GetStartupItemsAsync(10, CancellationToken.None);

        var item = Assert.Single(result);
        Assert.Equal("Healthy", item.Name);
    }

    [Fact]
    public async Task GetStartupItemsAsync_PropagatesCancellation()
    {
        var reader = new FakeStartupEntryReader(Candidate("Slow", StartupItemSource.CurrentUserRunRegistry))
        {
            WaitForCancellation = true
        };
        var service = new WindowsStartupItemService(reader);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.GetStartupItemsAsync(10, cancellation.Token));
    }

    [Fact]
    public async Task GetStartupItemsAsync_PreservesUnavailableOptionalFieldsWithoutFlaggingRisk()
    {
        var reader = new FakeStartupEntryReader(Candidate("UnknownPublisher", StartupItemSource.CurrentUserRunRegistry));
        var service = new WindowsStartupItemService(reader);

        var result = await service.GetStartupItemsAsync(10, CancellationToken.None);

        var item = Assert.Single(result);
        Assert.Null(item.Publisher);
        Assert.Null(item.IsSigned);
        Assert.Null(item.IsEnabled);
        Assert.Null(item.ExecutablePath);
        Assert.Equal(RiskLevel.Low, item.RiskLevel);
        Assert.NotNull(item.Notes);
    }

    [Fact]
    public async Task GetStartupItemsAsync_RejectsNonPositiveLimit()
    {
        var reader = new FakeStartupEntryReader();
        var service = new WindowsStartupItemService(reader);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.GetStartupItemsAsync(0, CancellationToken.None));
    }

    private static StartupEntryCandidate Candidate(string name, StartupItemSource source) => new()
    {
        Name = name,
        Source = source,
        RawCommand = null
    };

    private sealed class FakeStartupEntryReader : IStartupEntryReader
    {
        private readonly IReadOnlyList<StartupEntryCandidate> _candidates;

        public FakeStartupEntryReader(params StartupEntryCandidate[] candidates)
        {
            _candidates = candidates;
        }

        public string? ThrowForName { get; init; }

        public bool WaitForCancellation { get; init; }

        public IReadOnlyList<StartupEntryCandidate> GetCandidates() => _candidates;

        public async Task<StartupItem> InspectAsync(StartupEntryCandidate candidate, CancellationToken cancellationToken)
        {
            if (WaitForCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (candidate.Name == ThrowForName)
            {
                throw new InvalidOperationException("Simulated inspection failure.");
            }

            // Everything left unavailable/null deliberately mirrors a real
            // entry whose publisher/signature/enabled state cannot be determined.
            return new StartupItem
            {
                Name = candidate.Name,
                Source = candidate.Source,
                ExecutablePath = null,
                Publisher = null,
                IsSigned = null,
                IsEnabled = null,
                RiskLevel = RiskLevel.Low,
                Notes = "发布者或签名状态未知；未知本身不代表该项是恶意软件。"
            };
        }
    }
}
