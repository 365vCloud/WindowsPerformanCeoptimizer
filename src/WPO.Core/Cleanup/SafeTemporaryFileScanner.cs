using WPO.Core.Security;
using WPO.Domain.Enums;
using WPO.Domain.Models;

namespace WPO.Core.Cleanup;

/// <summary>
/// Read-only scanner for files in the current user's Temp directory. It reads
/// filesystem metadata only and never accepts or traverses reparse points.
/// </summary>
public sealed class SafeTemporaryFileScanner : ICleanupScanner
{
    private readonly IPathSafetyValidator _pathSafetyValidator;
    private readonly TemporaryFileScannerOptions _options;
    private readonly string _tempRoot;

    public SafeTemporaryFileScanner(
        IPathSafetyValidator pathSafetyValidator,
        TemporaryFileScannerOptions? options = null)
    {
        _pathSafetyValidator = pathSafetyValidator ?? throw new ArgumentNullException(nameof(pathSafetyValidator));
        _options = options ?? new TemporaryFileScannerOptions();

        if (string.IsNullOrWhiteSpace(_options.TempRoot))
        {
            throw new ArgumentException("A Temp root is required.", nameof(options));
        }

        if (_options.MinimumFileAge < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The minimum file age cannot be negative.");
        }

        if (_options.MaximumCandidateCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The maximum candidate count must be positive.");
        }

        _tempRoot = Path.GetFullPath(_options.TempRoot);
    }

    public CleanupCategory Category => CleanupCategory.TemporaryFiles;

    public Task<IReadOnlyList<CleanupItem>> ScanAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Scan(cancellationToken), cancellationToken);

    private IReadOnlyList<CleanupItem> Scan(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(_tempRoot) || IsReparsePoint(_tempRoot))
        {
            return Array.Empty<CleanupItem>();
        }

        var cutoffUtc = DateTimeOffset.UtcNow - _options.MinimumFileAge;
        var items = new List<CleanupItem>();
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        try
        {
            foreach (var path in Directory.EnumerateFiles(_tempRoot, "*", enumerationOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (items.Count >= _options.MaximumCandidateCount)
                {
                    break;
                }

                try
                {
                    var fullPath = Path.GetFullPath(path);
                    if (!IsUnderTempRoot(fullPath) || IsReparsePoint(fullPath))
                    {
                        continue;
                    }

                    var fileInfo = new FileInfo(fullPath);
                    var lastWriteTimeUtc = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero);
                    if (lastWriteTimeUtc > cutoffUtc)
                    {
                        continue;
                    }

                    var validation = _pathSafetyValidator.Validate(fullPath);
                    if (!validation.IsAllowed)
                    {
                        continue;
                    }

                    items.Add(new CleanupItem
                    {
                        FullPath = validation.NormalizedPath ?? fullPath,
                        Category = Category,
                        RiskLevel = RiskLevel.Low,
                        SizeBytes = fileInfo.Length,
                        LastModifiedUtc = lastWriteTimeUtc,
                        DetectedReason = "当前用户 Temp 目录中的过期临时文件（仅预览）"
                    });
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    // A volatile or inaccessible entry must not stop a safe preview.
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Unreadable portions of Temp yield the safely collected partial preview.
        }

        return items;
    }

    private bool IsUnderTempRoot(string candidate)
    {
        var root = _tempRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCandidate = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalizedCandidate.StartsWith(
            root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return true;
        }
    }
}
