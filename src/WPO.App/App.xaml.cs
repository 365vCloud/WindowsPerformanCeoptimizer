using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WPO.Core.DependencyInjection;

namespace WPO.App;

/// <summary>
/// WPF host wired to the preview-only current-user Temp scanner. The allow-list
/// is limited to the current user's Temp directory and no execution service is
/// exposed by the UI.
/// </summary>
public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddWindowsPerformanceOptimizerCore(options =>
        {
            options.AllowedRoots.Add(Path.GetTempPath());
        });

        Services = services.BuildServiceProvider();

        var mainWindow = new MainWindow(Services);
        mainWindow.Show();
    }
}
