using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using WPO.Core.Cleanup;
using WPO.Core.Audit;
using WPO.Core.Diagnostics;
using WPO.Core.Export;
using WPO.Core.Security;
using WPO.Core.Startup;
using WPO.Domain.Enums;
using WPO.Domain.Models;

namespace WPO.App;

/// <summary>
/// Dashboard for read-only diagnostics plus a review/confirm-only cleanup flow
/// for the current-user Temp preview. This window never deletes anything by
/// itself: selecting and "准备清理" only opens a separate modal confirmation
/// dialog (<see cref="ConfirmCleanupWindow"/>), and only after a second
/// explicit confirmation there does a modal progress window
/// (<see cref="CleanupExecutionWindow"/>) invoke the existing
/// <see cref="ICleanupExecutionService"/>. High-risk and validation-failed
/// items can never be selected; medium-risk items default to unselected and
/// require an explicit risk acknowledgement checkbox before they can be
/// included in a cleanup request.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly ObservableCollection<CleanupCandidateViewModel> _candidates = new();
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _diagnosticsCancellation;
    private CancellationTokenSource? _startupCheckCancellation;
    private CleanupPreviewResult? _lastPreview;

    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        CandidatesDataGrid.ItemsSource = _candidates;
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

    private async void PreviewButton_Click(object sender, RoutedEventArgs e) => await RefreshPreviewAsync();

    private async Task RefreshPreviewAsync()
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
            PopulateCandidates(result);
            StatusTextBlock.Text = "扫描完成。请勾选要清理的项目后点击“准备清理”进行确认。";
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

    private void PopulateCandidates(CleanupPreviewResult result)
    {
        _lastPreview = result;

        foreach (var vm in _candidates)
        {
            vm.PropertyChanged -= CandidateViewModel_PropertyChanged;
        }

        _candidates.Clear();

        var validator = _services.GetService(typeof(IPathSafetyValidator)) as IPathSafetyValidator;
        foreach (var item in result.Items)
        {
            // Re-validate right now (not just trusting the preview's own earlier
            // validation) so the UI can honestly show validation status even if
            // the filesystem changed between scan and display.
            var isValid = validator is null || validator.Validate(item.FullPath).IsAllowed;
            var vm = new CleanupCandidateViewModel(item, isValid);
            vm.PropertyChanged += CandidateViewModel_PropertyChanged;
            _candidates.Add(vm);
        }

        CandidateCountTextBlock.Text = result.TotalItemCount.ToString("N0");
        TotalSizeTextBlock.Text = FormatSize(result.TotalSizeBytes);
        ConfirmMediumRiskCheckBox.IsChecked = false;
        UpdateSelectionSummary();
    }

    private void CandidateViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CleanupCandidateViewModel.IsSelected))
        {
            UpdateSelectionSummary();
        }
    }

    private void UpdateSelectionSummary()
    {
        var selected = _candidates.Where(c => c.IsSelected).ToList();
        var totalBytes = selected.Sum(c => c.Item.SizeBytes);
        SelectionSummaryTextBlock.Text = $"已选择 {selected.Count} 项，共 {FormatSize(totalBytes)}";
        PrepareCleanupButton.IsEnabled = selected.Count > 0;
    }

    private void SelectAllSafeButton_Click(object sender, RoutedEventArgs e)
    {
        // "安全项目" intentionally means low-risk, currently-valid items only;
        // medium/high-risk items always require a separate, explicit action.
        foreach (var candidate in _candidates)
        {
            if (candidate.IsSelectable && candidate.Item.RiskLevel == RiskLevel.Low)
            {
                candidate.IsSelected = true;
            }
        }
    }

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var candidate in _candidates)
        {
            candidate.IsSelected = false;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        _scanCancellation?.Cancel();
        StatusTextBlock.Text = "正在取消扫描...";
    }

    private async void PrepareCleanupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastPreview is null)
        {
            return;
        }

        var selected = _candidates.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "请先勾选至少一个待清理项目。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Defense-in-depth: re-validate every selected path again, right before
        // opening the confirmation dialog, in addition to the execution
        // service's own mandatory re-validation before deletion.
        var validator = _services.GetService(typeof(IPathSafetyValidator)) as IPathSafetyValidator;
        var invalidated = new List<CleanupCandidateViewModel>();
        foreach (var candidate in selected)
        {
            var stillValid = validator is null || validator.Validate(candidate.Item.FullPath).IsAllowed;
            if (!stillValid)
            {
                candidate.MarkInvalid();
                candidate.IsSelected = false;
                invalidated.Add(candidate);
            }
        }

        if (invalidated.Count > 0)
        {
            UpdateSelectionSummary();
            MessageBox.Show(
                this,
                $"{invalidated.Count} 个已选项目在准备清理前的重新安全校验中未通过，已自动取消勾选，不会被清理。请重新扫描后再试。",
                "安全校验未通过",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var hasHighRiskSelected = selected.Any(c => c.Item.RiskLevel == RiskLevel.High);
        if (hasHighRiskSelected)
        {
            // High-risk items are never selectable from the grid; this only
            // guards against a future regression.
            MessageBox.Show(this, "高风险项目不允许通过此界面清理。", "已阻止", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var hasMediumRiskSelected = selected.Any(c => c.Item.RiskLevel == RiskLevel.Medium);
        if (hasMediumRiskSelected && ConfirmMediumRiskCheckBox.IsChecked != true)
        {
            MessageBox.Show(
                this,
                "所选项目包含中风险文件，请先勾选“我已确认包含中风险清理项”后再准备清理。",
                "需要风险确认",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var totalBytes = selected.Sum(c => c.Item.SizeBytes);
        var confirmWindow = new ConfirmCleanupWindow(selected.Count, totalBytes, hasMediumRiskSelected)
        {
            Owner = this
        };

        var confirmed = confirmWindow.ShowDialog();
        if (confirmed != true)
        {
            // Cancel, closing the dialog, Escape, or Enter all land here and
            // never execute anything.
            return;
        }

        var executionService = _services.GetService(typeof(ICleanupExecutionService)) as ICleanupExecutionService;
        if (executionService is null)
        {
            MessageBox.Show(this, "清理执行服务未注册。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var selection = new CleanupSelection
        {
            SelectedItemIds = selected.Select(c => c.Item.Id).ToHashSet(),
            ConfirmMediumRisk = hasMediumRiskSelected,
            ConfirmHighRisk = false,
            DeletionMode = DeletionMode.RecycleBin,
            ConfirmPermanentDeletion = false
        };

        var exportService = _services.GetService(typeof(IExportService)) as IExportService;
        var auditLogService = _services.GetService(typeof(IAuditLogService)) as IAuditLogService;
        if (exportService is null || auditLogService is null)
        {
            MessageBox.Show(this, "结果报告服务未注册。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var executionWindow = new CleanupExecutionWindow(executionService, _lastPreview, selection, exportService, auditLogService)
        {
            Owner = this
        };
        executionWindow.ShowDialog();

        // The filesystem has now changed (items may have moved to the Recycle
        // Bin); refresh the preview so stale rows are never shown as if they
        // were still selectable.
        await RefreshPreviewAsync();
    }

    private async void StartupCheckButton_Click(object sender, RoutedEventArgs e)
    {
        StartupCheckButton.IsEnabled = false;
        CancelStartupCheckButton.IsEnabled = true;
        _startupCheckCancellation = new CancellationTokenSource();
        StartupStatusTextBlock.Text = "正在检查启动项...";

        try
        {
            var startupItemService = _services.GetService(typeof(IStartupItemService)) as IStartupItemService;
            if (startupItemService is null)
            {
                StartupItemsDataGrid.ItemsSource = Array.Empty<StartupItemRow>();
                StartupItemCountTextBlock.Text = "0";
                StartupStatusTextBlock.Text = "启动项检查服务在当前平台不可用。";
                return;
            }

            var items = await startupItemService.GetStartupItemsAsync(200, _startupCheckCancellation.Token);
            StartupItemsDataGrid.ItemsSource = items.Select(item => new StartupItemRow(item));
            StartupItemCountTextBlock.Text = items.Count.ToString("N0");
            StartupStatusTextBlock.Text = "检查完成。仅供参考，未修改任何注册表项或启动项。";
        }
        catch (OperationCanceledException)
        {
            StartupStatusTextBlock.Text = "启动项检查已取消。";
        }
        catch (Exception ex)
        {
            StartupItemsDataGrid.ItemsSource = Array.Empty<StartupItemRow>();
            StartupItemCountTextBlock.Text = "0";
            StartupStatusTextBlock.Text = $"检查失败：{ex.Message}";
        }
        finally
        {
            _startupCheckCancellation.Dispose();
            _startupCheckCancellation = null;
            StartupCheckButton.IsEnabled = true;
            CancelStartupCheckButton.IsEnabled = false;
        }
    }

    private void CancelStartupCheckButton_Click(object sender, RoutedEventArgs e)
    {
        CancelStartupCheckButton.IsEnabled = false;
        _startupCheckCancellation?.Cancel();
        StartupStatusTextBlock.Text = "正在取消检查...";
    }

    private static string FormatSize(long sizeBytes) => SizeFormatter.Format(sizeBytes);

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

    /// <summary>
    /// UI-only wrapper around a <see cref="CleanupItem"/> that tracks the
    /// checkbox selection state and whether the item is currently allowed to
    /// be selected at all. Never performs any deletion itself.
    /// </summary>
    private sealed class CleanupCandidateViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isValid;

        public CleanupCandidateViewModel(CleanupItem item, bool isValid)
        {
            Item = item;
            _isValid = isValid;
        }

        public CleanupItem Item { get; }

        public bool IsValid => _isValid;

        /// <summary>
        /// High-risk items and items that failed (re-)validation can never be
        /// selected. Medium-risk items are selectable but default to
        /// unselected and additionally require the window-level risk
        /// acknowledgement checkbox before a cleanup request can proceed.
        /// </summary>
        public bool IsSelectable => _isValid && Item.RiskLevel != RiskLevel.High;

        public string SelectableReason => (_isValid, Item.RiskLevel) switch
        {
            (false, _) => "未通过安全校验，无法清理。",
            (true, RiskLevel.High) => "高风险项目不允许通过此界面清理。",
            (true, RiskLevel.Medium) => "中风险项目：勾选后仍需在下方确认风险才能准备清理。",
            _ => "低风险项目，可安全清理。"
        };

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public string FullPath => Item.FullPath;

        public string DisplaySize => FormatSize(Item.SizeBytes);

        public string RiskDisplay => Item.RiskLevel switch
        {
            RiskLevel.Low => "低风险",
            RiskLevel.Medium => "中风险",
            RiskLevel.High => "高风险",
            _ => "未知"
        };

        public string ValidationDisplay => _isValid ? "验证通过" : "验证失败（不可清理）";

        public void MarkInvalid()
        {
            if (_isValid)
            {
                _isValid = false;
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(IsSelectable));
                OnPropertyChanged(nameof(ValidationDisplay));
                OnPropertyChanged(nameof(SelectableReason));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

    private sealed record StartupItemRow(
        string Name,
        string ExecutablePath,
        string SourceDisplay,
        string EnabledDisplay,
        string SignatureDisplay)
    {
        public StartupItemRow(StartupItem item)
            : this(
                item.Name ?? "暂时无法获取",
                item.ExecutablePath ?? "暂时无法获取",
                item.Source switch
                {
                    StartupItemSource.CurrentUserRunRegistry => "当前用户注册表 (Run)",
                    StartupItemSource.LocalMachineRunRegistry => "本机注册表 (Run)",
                    StartupItemSource.CurrentUserStartupFolder => "当前用户启动文件夹",
                    StartupItemSource.AllUsersStartupFolder => "所有用户启动文件夹",
                    _ => "未知来源"
                },
                item.IsEnabled switch { true => "已启用", false => "已禁用", null => "暂时无法获取" },
                item.IsSigned switch
                {
                    true => item.Publisher is null ? "已签名" : $"已签名（{item.Publisher}）",
                    false => "未检测到签名",
                    null => "暂时无法获取"
                })
        {
        }
    }
}
