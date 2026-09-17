using Microsoft.Extensions.DependencyInjection;
using WPO.Core.Audit;
using WPO.Core.Cleanup;
using WPO.Core.RecycleBin;
using WPO.Core.Security;

namespace WPO.Core.DependencyInjection;

/// <summary>
/// Registers the core cleanup services (safety validator, preview/execution
/// services, recycle bin abstraction, and audit subsystem) into an
/// <see cref="IServiceCollection"/>, including the conservative current-user
/// Temp scanner used by the preview-only application.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWindowsPerformanceOptimizerCore(
        this IServiceCollection services,
        Action<PathSafetyOptions>? configurePathSafety = null)
    {
        var options = new PathSafetyOptions();
        configurePathSafety?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IPathSafetyValidator, PathSafetyValidator>();
        services.AddSingleton<IAuditLogMasker, AuditLogMasker>();
        services.AddSingleton<IAuditLogger, AuditLogger>();
        services.AddSingleton<IAuditLogExporter, AuditLogExporter>();
        services.AddSingleton<TemporaryFileScannerOptions>();
        services.AddSingleton<ICleanupScanner, SafeTemporaryFileScanner>();
        services.AddSingleton<ICleanupPreviewService, CleanupPreviewService>();
        services.AddSingleton<ICleanupExecutionService, CleanupExecutionService>();

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IRecycleBinService, WindowsRecycleBinService>();
        }

        return services;
    }
}
