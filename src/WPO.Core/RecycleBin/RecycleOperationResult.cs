namespace WPO.Core.RecycleBin;

/// <summary>
/// Result of attempting to move a single item to the Recycle Bin (or, when
/// explicitly requested and confirmed, permanently delete it).
/// </summary>
public sealed record RecycleOperationResult
{
    public required bool Succeeded { get; init; }

    public string? ErrorMessage { get; init; }

    public static RecycleOperationResult Success() => new() { Succeeded = true };

    public static RecycleOperationResult Failure(string errorMessage) => new()
    {
        Succeeded = false,
        ErrorMessage = errorMessage
    };
}
