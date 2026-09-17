namespace WPO.Core.Security;

/// <summary>
/// Default <see cref="IPathSafetyValidator"/> implementation. Every check is
/// deny-by-default: a path is only allowed once it has survived traversal,
/// drive-root, denied-root, allow-list and (optionally) symlink-escape checks.
/// </summary>
public sealed class PathSafetyValidator : IPathSafetyValidator
{
    private readonly PathSafetyOptions _options;

    public PathSafetyValidator(PathSafetyOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public PathValidationResult Validate(string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return PathValidationResult.Reject("EmptyPath");
        }

        if (ContainsTraversalSegment(candidatePath))
        {
            return PathValidationResult.Reject("TraversalSegment");
        }

        string normalized;
        try
        {
            normalized = TrimTrailingSeparators(Path.GetFullPath(candidatePath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return PathValidationResult.Reject("InvalidPath");
        }

        if (IsDriveOrRootPath(normalized))
        {
            return PathValidationResult.Reject("DriveRootDeletionForbidden", normalized);
        }

        if (IsUnderAnyRoot(normalized, _options.DeniedRoots))
        {
            return PathValidationResult.Reject("DeniedRoot", normalized);
        }

        if (!IsUnderAnyRoot(normalized, _options.AllowedRoots))
        {
            return PathValidationResult.Reject("NotInAllowList", normalized);
        }

        if (_options.ResolveSymbolicLinks)
        {
            var resolvedTarget = TryResolveFinalTarget(normalized);
            if (resolvedTarget is not null && !PathEquals(resolvedTarget, normalized))
            {
                var resolvedNormalized = TrimTrailingSeparators(resolvedTarget);
                if (IsUnderAnyRoot(resolvedNormalized, _options.DeniedRoots) ||
                    !IsUnderAnyRoot(resolvedNormalized, _options.AllowedRoots))
                {
                    return PathValidationResult.Reject("SymlinkEscape", normalized);
                }
            }
        }

        return PathValidationResult.Allow(normalized);
    }

    private static bool ContainsTraversalSegment(string path)
    {
        var segments = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(s => s == "..");
    }

    private static string TrimTrailingSeparators(string path) => path.TrimEnd('\\', '/');

    private static bool IsDriveOrRootPath(string normalizedPath)
    {
        var root = Path.GetPathRoot(normalizedPath);
        return !string.IsNullOrEmpty(root) && PathEquals(normalizedPath, TrimTrailingSeparators(root));
    }

    private static bool PathEquals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool IsUnderAnyRoot(string candidate, IEnumerable<string> roots)
    {
        foreach (var rawRoot in roots)
        {
            if (string.IsNullOrWhiteSpace(rawRoot))
            {
                continue;
            }

            string normalizedRoot;
            try
            {
                normalizedRoot = TrimTrailingSeparators(Path.GetFullPath(rawRoot));
            }
            catch (Exception)
            {
                continue;
            }

            if (IsUnder(candidate, normalizedRoot))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Segment-aware "is candidate at or below root" check. Appending a directory
    /// separator to <paramref name="root"/> before the prefix comparison is what
    /// prevents "similar prefix" bypasses such as an allowed root of
    /// "C:\Temp\Cache" incorrectly matching the sibling folder "C:\Temp\CacheOld".
    /// </summary>
    private static bool IsUnder(string candidate, string root)
    {
        if (PathEquals(candidate, root))
        {
            return true;
        }

        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Best-effort symlink/junction resolution. Returns null when the path does not
    /// exist or is not a reparse point, in which case no further re-validation is needed.
    /// </summary>
    private static string? TryResolveFinalTarget(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                return info.LinkTarget is null ? null : info.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
            }

            if (Directory.Exists(path))
            {
                var info = new DirectoryInfo(path);
                return info.LinkTarget is null ? null : info.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
            }
        }
        catch (IOException)
        {
            // Broken/unresolvable link: nothing to re-validate against, caller keeps normalized path checks.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }
}

