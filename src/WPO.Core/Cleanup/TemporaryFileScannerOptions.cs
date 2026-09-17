namespace WPO.Core.Cleanup;

/// <summary>
/// Conservative limits for <see cref="SafeTemporaryFileScanner"/>.
/// </summary>
public sealed class TemporaryFileScannerOptions
{
    /// <summary>
    /// Temp directory to scan. Defaults to the current user's Temp directory.
    /// This exists primarily to allow hosts and tests to supply an isolated root.
    /// </summary>
    public string TempRoot { get; init; } = Path.GetTempPath();

    /// <summary>
    /// Files modified more recently than this are not candidates.
    /// </summary>
    public TimeSpan MinimumFileAge { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Maximum number of candidates returned by a single scan.
    /// </summary>
    public int MaximumCandidateCount { get; init; } = 1_000;
}
