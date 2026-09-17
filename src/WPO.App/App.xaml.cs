using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
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
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WindowsPerformanceOptimizer",
        "logs");

    public IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Any unhandled exception here would otherwise terminate the process
        // with no visible feedback (perceived by users as the app "flashing"
        // and disappearing). Hook every relevant unhandled-exception surface
        // before doing any startup work so a diagnosable log is always
        // written, and show a message instead of silently vanishing.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            var services = new ServiceCollection();
            services.AddWindowsPerformanceOptimizerCore(options =>
            {
                options.AllowedRoots.Add(Path.GetTempPath());
            });

            Services = services.BuildServiceProvider();

            var mainWindow = new MainWindow(Services);
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            LogFatalException("Startup", ex);
            ShowFatalErrorMessage(ex);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogFatalException("UI thread", e.Exception);
        ShowFatalErrorMessage(e.Exception);
        e.Handled = true;
        Shutdown(1);
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogFatalException("Background thread", ex);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogFatalException("Unobserved task", e.Exception);
        e.SetObserved();
    }

    private static void ShowFatalErrorMessage(Exception ex)
    {
        MessageBox.Show(
            $"应用程序启动时遇到未处理的错误，已记录到日志目录：\n{LogDirectory}\n\n{ex.GetType().Name}: {ex.Message}",
            "Windows 性能优化器 - 启动失败",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    /// <summary>
    /// Writes a best-effort crash log to LocalAppData so a "flash and close"
    /// style failure can be diagnosed after the fact. Only exception
    /// type/message/stack trace are recorded (no user data, file contents,
    /// or credentials), and any failure while logging is swallowed so
    /// logging itself never crashes the process further.
    /// </summary>
    private static void LogFatalException(string source, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var logPath = Path.Combine(LogDirectory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");

            var builder = new StringBuilder();
            builder.AppendLine($"Timestamp: {DateTime.Now:O}");
            builder.AppendLine($"Source: {source}");
            builder.AppendLine($"OS: {Environment.OSVersion}");
            builder.AppendLine($".NET: {Environment.Version}");
            builder.AppendLine("Exception:");
            builder.AppendLine(ex.ToString());

            File.WriteAllText(logPath, builder.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Logging is best-effort; never let it mask the original error.
        }
    }
}
