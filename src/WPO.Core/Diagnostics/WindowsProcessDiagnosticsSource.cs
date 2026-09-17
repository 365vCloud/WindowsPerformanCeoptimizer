using System.Diagnostics;
using System.ComponentModel;
using WPO.Domain.Models;

namespace WPO.Core.Diagnostics;

/// <summary>Windows process reader that only inspects public process metadata.</summary>
public sealed class WindowsProcessDiagnosticsSource : IProcessDiagnosticsSource
{
    private static readonly TimeSpan CpuSampleInterval = TimeSpan.FromMilliseconds(200);

    public IReadOnlyList<int> GetProcessIds()
    {
        var processes = Process.GetProcesses();
        try
        {
            return processes.Select(process => process.Id).ToArray();
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    public async Task<ProcessDiagnostic?> InspectAsync(int processId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var process = Process.GetProcessById(processId);
            var name = TryGetReference(() => process.ProcessName);
            var memory = TryGetValue(() => process.WorkingSet64);
            var executablePath = TryGetReference(() => process.MainModule?.FileName);
            var publisher = executablePath is null
                ? null
                : TryGetReference(() => FileVersionInfo.GetVersionInfo(executablePath).CompanyName);
            var cpu = await SampleCpuUsageAsync(process, cancellationToken).ConfigureAwait(false);

            return new ProcessDiagnostic
            {
                Name = name,
                ProcessId = processId,
                MemoryBytes = memory.HasValue
                    ? MetricValue<long>.Available(memory.Value)
                    : MetricValue<long>.Unavailable("进程内存暂时无法获取"),
                CpuUsagePercent = cpu.HasValue
                    ? MetricValue<double>.Available(cpu.Value)
                    : MetricValue<double>.Unavailable("进程 CPU 使用率暂时无法获取"),
                ExecutablePath = executablePath,
                Publisher = publisher,
                IsSigned = null
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<double?> SampleCpuUsageAsync(Process process, CancellationToken cancellationToken)
    {
        var initial = TryGetValue(() => process.TotalProcessorTime);
        if (!initial.HasValue)
        {
            return null;
        }

        await Task.Delay(CpuSampleInterval, cancellationToken).ConfigureAwait(false);
        var final = TryGetValue(() => process.TotalProcessorTime);
        if (!final.HasValue)
        {
            return null;
        }

        return Math.Clamp(
            (final.Value - initial.Value).TotalMilliseconds / (CpuSampleInterval.TotalMilliseconds * Environment.ProcessorCount) * 100,
            0,
            100);
    }

    private static T? TryGetReference<T>(Func<T> getter)
        where T : class?
    {
        try
        {
            return getter();
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static T? TryGetValue<T>(Func<T> getter)
        where T : struct
    {
        try
        {
            return getter();
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
