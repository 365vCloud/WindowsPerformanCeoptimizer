using System.Windows;
using WPO.Core.Audit;

namespace WPO.App;

public partial class AuditLogWindow : Window
{
    private readonly IAuditLogService _auditLogService;

    public AuditLogWindow(IAuditLogService auditLogService)
    {
        InitializeComponent();
        _auditLogService = auditLogService;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        try
        {
            EntriesDataGrid.ItemsSource = await _auditLogService.ReadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"无法读取本地审计日志：{ex.Message}", "读取失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "确定清除所有本地审计日志吗？此操作不会影响清理结果窗口。", "确认清除日志", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _auditLogService.ClearAsync(confirmed: true);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"无法清除本地审计日志：{ex.Message}", "清除失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
