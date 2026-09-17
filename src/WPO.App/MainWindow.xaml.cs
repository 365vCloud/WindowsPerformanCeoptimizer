using System.Windows;
using WPO.Core.Cleanup;

namespace WPO.App;

/// <summary>
/// Interaction logic for MainWindow.xaml. This is a minimal skeleton: it can
/// only build a preview (no scanners are registered yet, so it always reports
/// zero candidates) and never performs any deletion. It exists to prove the
/// WPF host wires correctly into WPO.Core's dependency injection setup.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IServiceProvider _services;

    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    private async void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        PreviewButton.IsEnabled = false;
        try
        {
            var previewService = _services.GetService(typeof(ICleanupPreviewService)) as ICleanupPreviewService;
            if (previewService is null)
            {
                StatusTextBox.Text = "预览服务未注册。";
                return;
            }

            var result = await previewService.BuildPreviewAsync(CancellationToken.None);
            StatusTextBox.Text =
                $"预览完成：{result.TotalItemCount} 个候选项，共 {result.TotalSizeBytes} 字节。\r\n" +
                "尚未接入任何扫描器，因此此骨架不会发现或删除任何真实文件。";
        }
        catch (Exception ex)
        {
            StatusTextBox.Text = $"预览失败：{ex.Message}";
        }
        finally
        {
            PreviewButton.IsEnabled = true;
        }
    }
}
