using WPO.Core.RecycleBin;

namespace WPO.Core.Tests.TestSupport;

/// <summary>
/// In-memory fake of <see cref="IRecycleBinService"/> that never touches the
/// real filesystem. Used by every execution test so no test can accidentally
/// perform a real deletion.
/// </summary>
public sealed class FakeRecycleBinService : IRecycleBinService
{
    public List<string> MovedToRecycleBin { get; } = new();

    public List<string> PermanentlyDeleted { get; } = new();

    public HashSet<string> PathsThatFail { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? PathThatThrowsCancellation { get; set; }

    /// <summary>Invoked before each move attempt; tests use this to trigger external cancellation mid-run.</summary>
    public Action<string>? OnMoveInvoked { get; set; }

    public Task<RecycleOperationResult> MoveToRecycleBinAsync(string fullPath, CancellationToken cancellationToken)
    {
        OnMoveInvoked?.Invoke(fullPath);

        if (string.Equals(fullPath, PathThatThrowsCancellation, StringComparison.OrdinalIgnoreCase))
        {
            throw new OperationCanceledException();
        }

        if (PathsThatFail.Contains(fullPath))
        {
            return Task.FromResult(RecycleOperationResult.Failure("Simulated recycle bin failure."));
        }

        MovedToRecycleBin.Add(fullPath);
        return Task.FromResult(RecycleOperationResult.Success());
    }

    public Task<RecycleOperationResult> DeletePermanentlyAsync(string fullPath, CancellationToken cancellationToken)
    {
        if (string.Equals(fullPath, PathThatThrowsCancellation, StringComparison.OrdinalIgnoreCase))
        {
            throw new OperationCanceledException();
        }

        if (PathsThatFail.Contains(fullPath))
        {
            return Task.FromResult(RecycleOperationResult.Failure("Simulated permanent delete failure."));
        }

        PermanentlyDeleted.Add(fullPath);
        return Task.FromResult(RecycleOperationResult.Success());
    }
}

