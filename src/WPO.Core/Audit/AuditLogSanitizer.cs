using System.Text.RegularExpressions;
using WPO.Domain.Models;

namespace WPO.Core.Audit;

internal static partial class AuditLogSanitizer
{
    public static AuditLogEntry Sanitize(AuditLogEntry entry, IAuditLogMasker masker) => entry with
    {
        MaskedPath = entry.MaskedPath is null ? null : masker.Mask(Flatten(entry.MaskedPath)),
        Message = MaskSensitiveMessage(Flatten(entry.Message), masker)
    };

    private static string MaskSensitiveMessage(string message, IAuditLogMasker masker)
    {
        var withoutSecrets = SecretValuePattern().Replace(message, "$1$2<redacted>");
        var withoutPaths = WindowsPathPattern().Replace(withoutSecrets, "<path>");
        return masker.Mask(withoutPaths);
    }

    private static string Flatten(string value) =>
        value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

    [GeneratedRegex(@"(?i)\b(password|token|secret|api[-_]?key)\b\s*([:=])\s*\S+")]
    private static partial Regex SecretValuePattern();

    [GeneratedRegex(@"(?i)(?:[a-z]:[\\/]|\\\\)[^\s\r\n]+")]
    private static partial Regex WindowsPathPattern();
}
