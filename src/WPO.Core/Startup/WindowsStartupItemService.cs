using WPO.Domain.Models;

namespace WPO.Core.Startup;

/// <summary>
/// Bounded, read-only startup item scan. Collects candidates from the injected
/// <see cref="IStartupEntryReader"/>, inspects each one in isolation (a single
/// bad entry never fails the whole scan), then sorts and truncates the result.
/// Performs no writes, no process termination, and no shell-outs of any kind.
/// </summary>
public sealed class WindowsStartupItemService : IStartupItemService
{
    private readonly IStartupEntryReader _reader;

    public WindowsStartupItemService(IStartupEntryReader reader)
    {
        _reader = reader;
    }

    public async Task<IReadOnlyList<StartupItem>> GetStartupItemsAsync(int maximumItems, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumItems);
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = _reader.GetCandidates();
        var items = new List<StartupItem>(candidates.Count);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = await InspectIsolatedAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        return items
            .OrderBy(item => item.Source)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maximumItems)
            .ToArray();
    }

    private async Task<StartupItem?> InspectIsolatedAsync(StartupEntryCandidate candidate, CancellationToken cancellationToken)
    {
        try
        {
            return await _reader.InspectAsync(candidate, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // A single entry's inspection failure (missing file, access denied,
            // malformed registry value, etc.) must never abort the whole scan.
            return null;
        }
    }
}
