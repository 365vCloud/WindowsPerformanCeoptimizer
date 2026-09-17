namespace WPO.Domain.Enums;

/// <summary>
/// Category of disk space that a cleanup item was discovered under.
/// </summary>
public enum CleanupCategory
{
    TemporaryFiles,
    RecycleBin,
    BrowserCache,
    WindowsUpdateCache,
    LogFiles,
    ThumbnailCache,
    MemoryDumps,
    DownloadsOldFiles
}
