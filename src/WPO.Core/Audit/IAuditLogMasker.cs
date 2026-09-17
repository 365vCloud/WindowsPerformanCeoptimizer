namespace WPO.Core.Audit;

/// <summary>
/// Masks personally-identifying segments (Windows username, user profile
/// directory) out of a filesystem path before it is ever logged or exported.
/// </summary>
public interface IAuditLogMasker
{
    string Mask(string rawPath);
}
