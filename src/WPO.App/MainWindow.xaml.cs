using System.Windows;
using WPO.Core.Cleanup;
using WPO.Domain.Enums;
using WPO.Domain.Models;

namespace WPO.App;

/// <summary>
/// Preview-only dashboard for safe Temp candidates. It deliberately exposes no
/// deletion or execution control.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IServiceProvider _services;
    private CancellationTokenSource? _scanCancellation;

    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    private async void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        PreviewButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        _scanCancellation = new CancellationTokenSource();
        StatusTextBlock.Text = "正在扫描...";
        try
        {
            var previewService = _services.GetService(typeof(ICleanupPreviewService)) as ICleanupPreviewService;
            if (previewService is null)
            {
                StatusTextBlock.Text = "预览服务未注册。";
                return;
            }

            var result = await previewService.BuildPreviewAsync(_scanCancellation.Token);
            CandidatesDataGrid.ItemsSource = result.Items.Select(item => new PreviewRow(item));
            CandidateCountTextBlock.Text = result.TotalItemCount.ToString("N0");
            TotalSizeTextBlock.Text = FormatSize(result.TotalSizeBytes);
            StatusTextBlock.Text = "扫描完成。结果仅供预览，未执行任何文件操作。";
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "扫描已取消。";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"预览失败：{ex.Message}";
        }
        finally
        {
            _scanCancellation.Dispose();
            _scanCancellation = null;
            PreviewButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        _scanCancellation?.Cancel();
        StatusTextBlock.Text = "正在取消扫描...";
    }

    private static string FormatSize(long sizeBytes)
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

    private sealed record PreviewRow(string FullPath, string DisplaySize, string Risk)
    {
        public PreviewRow(CleanupItem item)
            : this(item.FullPath, FormatSize(item.SizeBytes), item.RiskLevel switch
            {
                RiskLevel.Low => "低风险",
                RiskLevel.Medium => "中风险",
                RiskLevel.High => "高风险",
                _ => "未知"
            })
        {
        }
    }
}
