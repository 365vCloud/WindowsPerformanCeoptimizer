namespace WPO.Domain.Enums;

/// <summary>
/// Risk classification for a cleanup candidate. Higher risk requires explicit
/// user confirmation before the item can ever be included in an execution.
/// </summary>
public enum RiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2
}
