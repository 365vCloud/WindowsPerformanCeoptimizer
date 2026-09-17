using System.Windows;
using WPO.Core.Cleanup;
using WPO.Core.Diagnostics;
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
    private CancellationTokenSource? _diagnosticsCancellation;

    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    private async void RefreshDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshDiagnosticsButton.IsEnabled = false;
        CancelDiagnosticsButton.IsEnabled = true;
        _diagnosticsCancellation = new CancellationTokenSource();
        DiagnosticsStatusTextBlock.Text = "正在获取诊断信息...";

        try
        {
            var metricsService = _services.GetService(typeof(ISystemMetricsService)) as ISystemMetricsService;
            var scanService = _services.GetService(typeof(IPerformanceScanService)) as IPerformanceScanService;
            if (metricsService is null || scanService is null)
            {
                SetDiagnosticsUnavailable();
                return;
            }

            var metricsTask = metricsService.GetMetricsAsync(_diagnosticsCancellation.Token);
            var scanTask = scanService.ScanAsync(50, _diagnosticsCancellation.Token);
            await Task.WhenAll(metricsTask, scanTask);
            var metrics = await metricsTask;
            var scan = await scanTask;

            CpuTextBlock.Text = FormatPercent(metrics.CpuUsagePercent);
            MemoryTextBlock.Text = metrics.TotalMemoryBytes.IsAvailable && metrics.UsedMemoryBytes.IsAvailable
                ? $"{FormatSize(metrics.UsedMemoryBytes.Value!.Value)} / {FormatSize(metrics.TotalMemoryBytes.Value!.Value)}"
                : "暂时无法获取";
            DiskTextBlock.Text = metrics.FreeDiskBytes.IsAvailable
                ? $"{metrics.SystemDriveName} {FormatSize(metrics.FreeDiskBytes.Value!.Value)}"
                : "暂时无法获取";
            ProcessCountTextBlock.Text = scan.Processes.Count.ToString("N0");
            ProcessesDataGrid.ItemsSource = scan.Processes.Select(process => new ProcessRow(process));
            DiagnosticsStatusTextBlock.Text = "诊断信息已刷新。";
        }
        catch (OperationCanceledException)
        {
            DiagnosticsStatusTextBlock.Text = "诊断已取消。";
        }
        catch (Exception)
        {
            SetDiagnosticsUnavailable();
        }
        finally
        {
            _diagnosticsCancellation.Dispose();
            _diagnosticsCancellation = null;
            RefreshDiagnosticsButton.IsEnabled = true;
            CancelDiagnosticsButton.IsEnabled = false;
        }
    }

    private void CancelDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        CancelDiagnosticsButton.IsEnabled = false;
        _diagnosticsCancellation?.Cancel();
        DiagnosticsStatusTextBlock.Text = "正在取消诊断...";
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

    private static string FormatPercent(MetricValue<double> value) =>
        value.IsAvailable ? $"{value.Value!.Value:N1}%" : "暂时无法获取";

    private void SetDiagnosticsUnavailable()
    {
        CpuTextBlock.Text = "暂时无法获取";
        MemoryTextBlock.Text = "暂时无法获取";
        DiskTextBlock.Text = "暂时无法获取";
        ProcessCountTextBlock.Text = "0";
        ProcessesDataGrid.ItemsSource = Array.Empty<ProcessRow>();
        DiagnosticsStatusTextBlock.Text = "暂时无法获取";
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

    private sealed record ProcessRow(
        string Name,
        int ProcessId,
        string Memory,
        string Cpu,
        string ExecutablePath,
        string Publisher,
        string Signature)
    {
        public ProcessRow(ProcessDiagnostic process)
            : this(
                process.Name ?? "暂时无法获取",
                process.ProcessId,
                process.MemoryBytes.IsAvailable ? FormatSize(process.MemoryBytes.Value!.Value) : "暂时无法获取",
                FormatPercent(process.CpuUsagePercent),
                process.ExecutablePath ?? "暂时无法获取",
                process.Publisher ?? "暂时无法获取",
                process.IsSigned switch { true => "已签名", false => "未签名", null => "暂时无法获取" })
        {
        }
    }
}
