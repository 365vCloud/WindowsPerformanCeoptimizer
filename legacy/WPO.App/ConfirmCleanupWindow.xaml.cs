using System.Windows;
using System.Windows.Input;

namespace WPO.App;

/// <summary>
/// Modal, non-executing confirmation dialog shown before any cleanup is run.
/// Safety invariants:
/// <list type="bullet">
/// <item>Cancel, closing the window, and Escape all resolve to <see cref="Window.DialogResult"/>
/// of <c>false</c>/<c>null</c> and never trigger deletion.</item>
/// <item>Enter is swallowed at the window level so it can never activate the confirm button,
/// regardless of focus.</item>
/// <item>The confirm button is never given initial keyboard focus; focus starts on Cancel.</item>
/// <item>Clicking "确认清理" only sets <see cref="Window.DialogResult"/> to <c>true</c> after an
/// explicit second Yes/No confirmation (defaulting to No) is accepted by the user.</item>
/// <item>Only the Recycle Bin processing mode is ever presented; there is no permanent-delete option.</item>
/// </list>
/// </summary>
public partial class ConfirmCleanupWindow : Window
{
    public ConfirmCleanupWindow(int itemCount, long totalBytes, bool hasMediumRisk)
    {
        InitializeComponent();

        SummaryTextBlock.Text =
            $"将处理 {itemCount} 个已选项目，预计释放约 {SizeFormatter.Format(totalBytes)} 磁盘空间。";
        RiskTextBlock.Text = hasMediumRisk
            ? "所选项目中包含中风险文件，您已显式勾选风险确认；请再次核对后决定是否继续。"
            : "所选项目均为低风险的临时文件。";

        Loaded += (_, _) => CancelButton.Focus();
        PreviewKeyDown += ConfirmCleanupWindow_PreviewKeyDown;
    }

    private void ConfirmCleanupWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Enter must never confirm this dialog by default; only an explicit
        // mouse/keyboard click on the confirm button (below) can proceed.
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var secondConfirmation = MessageBox.Show(
            this,
            "这是第二次确认：确定要将所选项目移动到回收站吗？此操作不会永久删除文件，可从回收站中还原。",
            "二次确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (secondConfirmation == MessageBoxResult.Yes)
        {
            DialogResult = true;
        }
    }
}
