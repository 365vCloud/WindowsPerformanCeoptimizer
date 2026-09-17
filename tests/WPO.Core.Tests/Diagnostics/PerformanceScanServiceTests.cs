using WPO.Core.Diagnostics;
using WPO.Domain.Models;

namespace WPO.Core.Tests.Diagnostics;

public sealed class PerformanceScanServiceTests
{
    [Fact]
    public void MetricValue_Unavailable_DoesNotExposeAFabricatedValue()
    {
        var value = MetricValue<double>.Unavailable("CPU counter unavailable");

        Assert.False(value.IsAvailable);
        Assert.Null(value.Value);
        Assert.Equal("CPU counter unavailable", value.UnavailabilityReason);
    }

    [Fact]
    public async Task ScanAsync_SortsByAvailableMemoryAndLimitsResults()
    {
        var source = new FakeProcessSource(
            CreateProcess(1, "Low", 10),
            CreateProcess(2, "High", 100),
            CreateProcess(3, "Unavailable", null),
            CreateProcess(4, "Medium", 50));
        var service = new PerformanceScanService(source);

        var result = await service.ScanAsync(2, CancellationToken.None);

        Assert.Collection(
            result.Processes,
            process => Assert.Equal(2, process.ProcessId),
            process => Assert.Equal(4, process.ProcessId));
    }

    [Fact]
    public async Task ScanAsync_IsolatesSingleProcessInspectionFailures()
    {
        var source = new FakeProcessSource(
            CreateProcess(1, "Healthy", 10),
            CreateProcess(2, "Fails", 20))
        {
            ThrowForProcessId = 2
        };
        var service = new PerformanceScanService(source);

        var result = await service.ScanAsync(10, CancellationToken.None);

        var process = Assert.Single(result.Processes);
        Assert.Equal(1, process.ProcessId);
    }

    [Fact]
    public async Task ScanAsync_PropagatesCancellation()
    {
        var source = new FakeProcessSource(CreateProcess(1, "Slow", 10))
        {
            WaitForCancellation = true
        };
        var service = new PerformanceScanService(source);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.ScanAsync(10, cancellation.Token));
    }

    private static ProcessDiagnostic CreateProcess(int id, string name, long? memoryBytes) => new()
    {
        ProcessId = id,
        Name = name,
        MemoryBytes = memoryBytes.HasValue
            ? MetricValue<long>.Available(memoryBytes.Value)
            : MetricValue<long>.Unavailable("memory unavailable"),
        CpuUsagePercent = MetricValue<double>.Unavailable("cpu unavailable")
    };

    private sealed class FakeProcessSource : IProcessDiagnosticsSource
    {
        private readonly IReadOnlyDictionary<int, ProcessDiagnostic> _processes;

        public FakeProcessSource(params ProcessDiagnostic[] processes)
        {
            _processes = processes.ToDictionary(process => process.ProcessId);
        }

        public int? ThrowForProcessId { get; init; }

        public bool WaitForCancellation { get; init; }

        public IReadOnlyList<int> GetProcessIds() => _processes.Keys.ToArray();

        public async Task<ProcessDiagnostic?> InspectAsync(int processId, CancellationToken cancellationToken)
        {
            if (WaitForCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (processId == ThrowForProcessId)
            {
                throw new InvalidOperationException("The process exited.");
            }

            return _processes[processId];
        }
    }
}
