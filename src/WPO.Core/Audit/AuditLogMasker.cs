namespace WPO.Core.Audit;

/// <summary>
/// Default masker. Replaces the current user profile directory prefix, and any
/// standalone occurrence of the current Windows username as a path segment,
/// with the fixed placeholder "&lt;user&gt;" so audit logs and exports never
/// contain a real machine username.
/// </summary>
public sealed class AuditLogMasker : IAuditLogMasker
{
    private const string Placeholder = "<user>";

    private readonly string _userProfileRoot;
    private readonly string _userName;

    public AuditLogMasker()
        : this(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.UserName)
    {
    }

    public AuditLogMasker(string userProfileRoot, string userName)
    {
        _userProfileRoot = (userProfileRoot ?? string.Empty).TrimEnd('\\', '/');
        _userName = userName ?? string.Empty;
    }

    public string Mask(string rawPath)
    {
        if (string.IsNullOrEmpty(rawPath))
        {
            return rawPath;
        }

        var masked = rawPath;

        if (!string.IsNullOrEmpty(_userProfileRoot))
        {
            var withSeparator = _userProfileRoot + Path.DirectorySeparatorChar;
            if (masked.StartsWith(withSeparator, StringComparison.OrdinalIgnoreCase))
            {
                masked = Placeholder + masked[_userProfileRoot.Length..];
            }
            else if (string.Equals(masked, _userProfileRoot, StringComparison.OrdinalIgnoreCase))
            {
                masked = Placeholder;
            }
        }

        if (!string.IsNullOrEmpty(_userName))
        {
            var segments = masked.Split(Path.DirectorySeparatorChar);
            for (var i = 0; i < segments.Length; i++)
            {
                if (string.Equals(segments[i], _userName, StringComparison.OrdinalIgnoreCase))
                {
                    segments[i] = Placeholder;
                }
            }

            masked = string.Join(Path.DirectorySeparatorChar, segments);
        }

        return masked;
    }
}
