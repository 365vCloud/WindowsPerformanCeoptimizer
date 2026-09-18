using WPO.Domain.Enums;

namespace WPO.Domain.Models;

/// <summary>
/// User's selection of which preview items to act on, plus the explicit
/// confirmations required before medium/high risk items or permanent
/// deletion can be executed. Absence of a confirmation flag is the safe
/// default and causes matching items to be skipped rather than deleted.
/// </summary>
public sealed record CleanupSelection
{
    public required IReadOnlySet<Guid> SelectedItemIds { get; init; }

    /// <summary>Must be true for any Medium risk item in the selection to be executed.</summary>
    public bool ConfirmMediumRisk { get; init; }

    /// <summary>Must be true for any High risk item in the selection to be executed.</summary>
    public bool ConfirmHighRisk { get; init; }

    /// <summary>
    /// Defaults to <see cref="DeletionMode.RecycleBin"/>. Permanent deletion requires
    /// this to be explicitly set AND <see cref="ConfirmPermanentDeletion"/> to be true.
    /// </summary>
    public DeletionMode DeletionMode { get; init; } = DeletionMode.RecycleBin;

    /// <summary>Second, independent confirmation required only for permanent deletion.</summary>
    public bool ConfirmPermanentDeletion { get; init; }
}
