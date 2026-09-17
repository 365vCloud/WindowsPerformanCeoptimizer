using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WPO.Core.DependencyInjection;

namespace WPO.App;

/// <summary>
/// Minimal WPF host skeleton. Wires up the core services with a conservative
/// default allow-list (the current user's Temp directory only) so the app is
/// runnable out of the box without granting itself broad filesystem access.
/// No scanners are registered yet - that is left to a future iteration - so
/// this skeleton never discovers or deletes anything on its own.
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

