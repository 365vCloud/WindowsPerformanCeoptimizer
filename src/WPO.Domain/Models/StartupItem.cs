using WPO.Domain.Enums;

namespace WPO.Domain.Models;

/// <summary>
/// Read-only snapshot of one autorun entry. Fields that Windows cannot reliably
/// or safely provide (publisher, signature, enabled state, resolved path) are
/// nullable rather than guessed; an unknown publisher or signature must never
/// be treated as evidence of malware by any consumer of this model.
/// </summary>
public sealed record StartupItem
{
    /// <summary>Display/registry value name of the entry, when available.</summary>
    public string? Name { get; init; }

    /// <summary>Best-effort resolved executable/target path, when available.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Publisher/company name read from file metadata, when available.</summary>
    public string? Publisher { get; init; }

    /// <summary>
    /// Whether the executable carries a recognizable Authenticode signature.
    /// Null means the signature status could not be determined (not "unsigned").
    /// </summary>
    public bool? IsSigned { get; init; }

    /// <summary>
    /// Whether the entry currently runs at logon. Null means this could not be
    /// determined from the read-only sources inspected.
    /// </summary>
    public bool? IsEnabled { get; init; }

    /// <summary>Where this entry was discovered.</summary>
    public required StartupItemSource Source { get; init; }

    /// <summary>
    /// Informational risk classification. This tool never escalates risk merely
    /// because a publisher or signature is unknown/missing.
    /// </summary>
    public RiskLevel RiskLevel { get; init; } = RiskLevel.Low;

    /// <summary>Human-readable explanation shown alongside the risk classification.</summary>
    public string? Notes { get; init; }
}
