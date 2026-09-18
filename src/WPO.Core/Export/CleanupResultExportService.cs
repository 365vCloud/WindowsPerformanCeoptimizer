using System.Text;
using System.Text.Json;
using WPO.Domain.Models;

namespace WPO.Core.Export;

public sealed class CleanupResultExportService : IExportService
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task ExportCleanupResultAsync(CleanupExecutionResult result, string destinationPath, ExportFormat format, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("An export destination is required.", nameof(destinationPath));
        }

        var content = format == ExportFormat.Csv ? ToCsv(result) : JsonSerializer.Serialize(result, JsonOptions);
        await File.WriteAllTextAsync(destinationPath, content, Utf8WithoutBom, cancellationToken).ConfigureAwait(false);
    }

    private static string ToCsv(CleanupExecutionResult result)
    {
        var builder = new StringBuilder("FinalStatus,ScannedItemCount,SelectedItemCount,AttemptedItemCount,SuccessfulItemCount,SkippedItemCount,FailedItemCount,CancelledItemCount,EstimatedBytes,ActualBytesFreed\r\n");
        builder.AppendLine(string.Join(',', new[]
        {
            result.FinalStatus.ToString(), result.ScannedItemCount.ToString(), result.SelectedItemCount.ToString(),
            result.AttemptedItemCount.ToString(), result.SuccessfulItemCount.ToString(), result.SkippedItemCount.ToString(),
            result.FailedItemCount.ToString(), result.CancelledItemCount.ToString(), result.EstimatedBytes.ToString(), result.TotalBytesFreed.ToString()
        }));
        builder.AppendLine();
        builder.AppendLine("ItemId,Selected,Status,Reason,SizeBytes,Path,Details");
        foreach (var item in result.Items)
        {
            builder.AppendLine(string.Join(',', new[]
            {
                item.ItemId.ToString(), item.WasSelected.ToString(), item.Status.ToString(), item.Reason.ToString(),
                item.SizeBytes.ToString(), Escape(item.FullPath), Escape(item.ErrorMessage ?? string.Empty)
            }));
        }

        return builder.ToString();
    }

    private static string Escape(string value)
    {
        var safeValue = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' ? "'" + value : value;
        return safeValue.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{safeValue.Replace("\"", "\"\"")}\""
            : safeValue;
    }
}
