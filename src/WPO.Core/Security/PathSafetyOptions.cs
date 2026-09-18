namespace WPO.Core.Security;

/// <summary>
/// Configuration for <see cref="IPathSafetyValidator"/>. All roots are treated
/// as directory prefixes matched on full path *segments*, never raw string
/// prefixes, to prevent "similar prefix" bypasses (e.g. an allowed root of
/// "C:\Temp\Cache" must not match a sibling folder "C:\Temp\CacheOld").
/// </summary>
public sealed class PathSafetyOptions
{
    /// <summary>
    /// Directories that cleanup operations are allowed to touch. A candidate
    /// path must resolve to a location at or below one of these roots.
    /// </summary>
    public List<string> AllowedRoots { get; init; } = new();

    /// <summary>
    /// Directories that are always rejected, even if they happen to fall under
    /// an allowed root. Provides defense-in-depth against overly broad
    /// whitelist configuration mistakes.
    /// </summary>
    public List<string> DeniedRoots { get; init; } = new()
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    };

    /// <summary>
    /// When true (default), a reparse point (symlink/junction) encountered while
    /// resolving a candidate path is followed and the final resolved target is
    /// re-validated against the allow/deny lists, closing off symlink-escape attacks.
    /// </summary>
    public bool ResolveSymbolicLinks { get; init; } = true;
}
