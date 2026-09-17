namespace WPO.App;

/// <summary>
/// Shared human-readable byte size formatting used by the dashboard and the
/// cleanup confirmation/progress dialogs. Purely presentational; never used
/// for any size threshold decision that affects deletion behavior.
/// </summary>
internal static class SizeFormatter
{
    public static string Format(long sizeBytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)sizeBytes;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{size:N0} {units[unitIndex]}" : $"{size:N1} {units[unitIndex]}";
    }
}
