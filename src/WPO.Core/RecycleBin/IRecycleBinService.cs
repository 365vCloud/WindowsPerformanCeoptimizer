namespace WPO.Core.RecycleBin;

/// <summary>
/// Abstraction over the destructive part of a cleanup: moving a file/folder to
/// the Recycle Bin, or (only when explicitly requested) permanently deleting it.
/// Kept as an abstraction so the execution service can be unit tested with a
/// fake implementation that never touches the real filesystem.
/// </summary>
public interface IRecycleBinService
{
    /// <summary>Moves the item at <paramref name="fullPath"/> to the Recycle Bin. This is the safe default.</summary>
    Task<RecycleOperationResult> MoveToRecycleBinAsync(string fullPath, CancellationToken cancellationToken);

    /// <summary>
    /// Permanently deletes the item at <paramref name="fullPath"/>. Callers must have
    /// already obtained an explicit, separate user confirmation before invoking this -
    /// this abstraction performs no confirmation itself.
    /// </summary>
    Task<RecycleOperationResult> DeletePermanentlyAsync(string fullPath, CancellationToken cancellationToken);
}
