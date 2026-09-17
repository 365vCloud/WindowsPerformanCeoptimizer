using WPO.Domain.Models;

namespace WPO.Core.Startup;

/// <summary>
/// Read-only, bounded enumeration of common Windows autorun entries. No
/// implementation may write the registry, modify shortcuts/files, end
/// processes, or invoke PowerShell/other shell-outs.
/// </summary>
public interface IStartupItemService
{
    Task<IReadOnlyList<StartupItem>> GetStartupItemsAsync(int maximumItems, CancellationToken cancellationToken);
}
