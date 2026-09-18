namespace WPO.Domain.Enums;

/// <summary>
/// How a deletion is physically carried out. Permanent deletion is never chosen
/// automatically by the system; it requires an explicit, separately confirmed
/// user action for every execution request.
/// </summary>
public enum DeletionMode
{
    /// <summary>Default and only automatic mode: items are moved to the Recycle Bin.</summary>
    RecycleBin,

    /// <summary>Permanent deletion. Only allowed when the caller supplies an explicit second confirmation.</summary>
    PermanentWithConfirmation
}
