using System.ComponentModel;
using System.Linq;
using System.Windows;
using WPO.Core.Cleanup;
using WPO.Domain.Enums;
using WPO.Domain.Models;

namespace WPO.App;

/// <summary>
/// Modal async progress/results window for a single cleanup execution run.
/// Starts the run only after this window is shown (never before an explicit,
/// twice-confirmed user action reaches this point), supports cancellation via
/// the existing <see cref="ICleanupExecutionService"/> cancellation token, and
/// reports success/skipped/failed/cancelled counts plus actual bytes freed
/// once the run finishes or is cancelled. A single item failure never stops
/// the run; remaining items continue to be processed.
/// </summary>
public partial class CleanupExecutionWindow : Window
{
    private readonly ICleanupExecutionService _executionService;
    private readonly CleanupPreviewResult _preview;
    private readonly CleanupSelection _selection;
    private CancellationTokenSource? _cts;

    public CleanupExecutionResult? Result { get; private set; }

    public CleanupExecutionWindow(
        ICleanupExecutionService executionService,
        CleanupPreviewResult preview,
        CleanupSelection selection)
    {
        InitializeComponent();
        _executionService = executionService;
        _preview = preview;
        _selection = selection;

        Loaded += CleanupExecutionWindow_Loaded;
        Closing += CleanupExecutionWindow_Closing;
    }

    private async void CleanupExecutionWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _cts = new CancellationTokenSource();

        try
        {
            var result = await _executionService.ExecuteAsync(_preview, _selection, _cts.Token);
            Result = result;
            ShowResults(result);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"清理执行失败：{ex.Message}";
            ProgressBarControl.IsIndeterminate = false;
            ProgressBarControl.Value = 0;
        }
        finally
        {
            CancelButton.IsEnabled = false;
            CloseButton.IsEnabled = true;
        }
    }

    private void ShowResults(CleanupExecutionResult result)
    {
        var deleted = result.Items.Count(i => i.Status == CleanupItemStatus.Deleted);
        var skipped = result.Items.Count(i => i.Status == CleanupItemStatus.Skipped);
        var failed = result.Items.Count(i => i.Status == CleanupItemStatus.Failed);
        var cancelled = result.Items.Count(i => i.Status == CleanupItemStatus.Cancelled);

        StatusTextBlock.Text =
            $"{(result.WasCancelled ? "清理已取消。" : "清理完成。")}" +
            $"成功 {deleted}，跳过 {skipped}，失败 {failed}，取消 {cancelled}，" +
            $"实际释放 {SizeFormatter.Format(result.TotalBytesFreed)}。";

        ProgressBarControl.IsIndeterminate = false;
        ProgressBarControl.Value = 100;
        ResultsDataGrid.Visibility = Visibility.Visible;
        ResultsDataGrid.ItemsSource = result.Items.Select(item => new ExecutionResultRow(item)).ToList();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        StatusTextBlock.Text = "正在取消清理...";
        _cts?.Cancel();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CleanupExecutionWindow_Closing(object? sender, CancelEventArgs e)
    {
        // Closing the window (including the title-bar X) must never leave a
        // run silently going on unattended; it simply requests cancellation
        // like the explicit Cancel button does.
        _cts?.Cancel();
    }

    private sealed record ExecutionResultRow(string FullPath, string DisplaySize, string StatusDisplay, string? ErrorMessage)
    {
        public ExecutionResultRow(CleanupExecutionItemResult item)
            : this(
                item.FullPath,
                SizeFormatter.Format(item.SizeBytes),
                item.Status switch
                {
                    CleanupItemStatus.Deleted => "已清理（回收站）",
                    CleanupItemStatus.Skipped => "已跳过",
                    CleanupItemStatus.Failed => "失败",
                    CleanupItemStatus.Cancelled => "已取消",
                    _ => item.Status.ToString()
                },
                item.ErrorMessage)
        {
        }
    }
}
