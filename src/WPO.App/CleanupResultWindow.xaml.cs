using Microsoft.Win32;
using System.Windows;
using WPO.Core.Audit;
using WPO.Core.Export;
using WPO.Domain.Enums;
using WPO.Domain.Models;

namespace WPO.App;

/// <summary>Read-only cleanup report view. All persistence is delegated to services.</summary>
public partial class CleanupResultWindow : Window
{
    private readonly CleanupExecutionResult _result;
    private readonly IExportService _exportService;
    private readonly IAuditLogService _auditLogService;
    private readonly string _summary;

    public CleanupResultWindow(CleanupExecutionResult result, IExportService exportService, IAuditLogService auditLogService)
    {
        InitializeComponent();
        _result = result;
        _exportService = exportService;
        _auditLogService = auditLogService;
        _summary = BuildSummary(result);
        StatusTextBlock.Text = result.FinalStatus switch
        {
            CleanupExecutionStatus.Cancelled => "清理已取消，以下为已完成项目的最终结果。",
            CleanupExecutionStatus.CompletedWithFailures => "清理已完成，但部分项目未能处理。",
            _ => "清理已完成。"
        };
        SummaryTextBlock.Text = _summary;
        ResultsDataGrid.ItemsSource = result.Items.Select(item => new ResultRow(item)).ToList();
    }

    private async void ExportCsvButton_Click(object sender, RoutedEventArgs e) => await ExportAsync(ExportFormat.Csv, "CSV 文件|*.csv", "cleanup-result.csv");

    private async void ExportJsonButton_Click(object sender, RoutedEventArgs e) => await ExportAsync(ExportFormat.Json, "JSON 文件|*.json", "cleanup-result.json");

    private async Task ExportAsync(ExportFormat format, string filter, string fileName)
    {
        var dialog = new SaveFileDialog { Filter = filter, FileName = fileName, AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await _exportService.ExportCleanupResultAsync(_result, dialog.FileName, format);
            MessageBox.Show(this, $"结果已导出到：{dialog.FileName}", "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"导出失败，当前结果仍可查看。原因：{ex.Message}", "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopySummaryButton_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_summary);
        MessageBox.Show(this, "摘要已复制到剪贴板。", "已复制", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ViewAuditLogButton_Click(object sender, RoutedEventArgs e) =>
        new AuditLogWindow(_auditLogService) { Owner = this }.ShowDialog();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private static string BuildSummary(CleanupExecutionResult result) =>
        $"扫描 {result.ScannedItemCount}，选择 {result.SelectedItemCount}，尝试 {result.AttemptedItemCount}，成功 {result.SuccessfulItemCount}，" +
        $"跳过 {result.SkippedItemCount}，失败 {result.FailedItemCount}，取消 {result.CancelledItemCount}。预计 {SizeFormatter.Format(result.EstimatedBytes)}，" +
        $"实际释放 {SizeFormatter.Format(result.TotalBytesFreed)}。最终状态：{result.FinalStatus}。";

    private sealed record ResultRow(string FullPath, bool WasSelected, string StatusDisplay, string DisplaySize, string Detail)
    {
        public ResultRow(CleanupExecutionItemResult item)
            : this(item.FullPath, item.WasSelected, item.Status.ToString(), SizeFormatter.Format(item.SizeBytes),
                item.ErrorMessage ?? item.Reason.ToString())
        {
        }
    }
}
