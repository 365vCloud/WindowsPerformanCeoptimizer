using System.Runtime.InteropServices;
using WPO.Domain.Models;

namespace WPO.Core.Diagnostics;

/// <summary>
/// Windows read-only metrics implementation. CPU is explicitly unavailable
/// because managed .NET exposes no reliable system-wide CPU counter.
/// </summary>
public sealed class WindowsSystemMetricsService : ISystemMetricsService
{
    public Task<SystemMetricsSnapshot> GetMetricsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var memoryStatus = new MemoryStatusEx();
        var hasMemoryStatus = GlobalMemoryStatusEx(ref memoryStatus);
        var systemDriveName = Path.GetPathRoot(Environment.SystemDirectory)!;
        MetricValue<long> freeDiskBytes;
        try
        {
            var systemDrive = new DriveInfo(systemDriveName);
            freeDiskBytes = MetricValue<long>.Available(systemDrive.AvailableFreeSpace);
        }
        catch (IOException)
        {
            freeDiskBytes = MetricValue<long>.Unavailable("系统盘剩余空间暂时无法获取");
        }
        catch (UnauthorizedAccessException)
        {
            freeDiskBytes = MetricValue<long>.Unavailable("系统盘剩余空间暂时无法获取");
        }

        var totalMemory = hasMemoryStatus
            ? MetricValue<long>.Available((long)memoryStatus.TotalPhysicalMemory)
            : MetricValue<long>.Unavailable("物理内存总量暂时无法获取");
        var usedMemory = hasMemoryStatus
            ? MetricValue<long>.Available((long)(memoryStatus.TotalPhysicalMemory - memoryStatus.AvailablePhysicalMemory))
            : MetricValue<long>.Unavailable("已用物理内存暂时无法获取");

        return Task.FromResult(new SystemMetricsSnapshot
        {
            CpuUsagePercent = MetricValue<double>.Unavailable("系统 CPU 使用率暂时无法获取"),
            TotalMemoryBytes = totalMemory,
            UsedMemoryBytes = usedMemory,
            SystemDriveName = systemDriveName,
            FreeDiskBytes = freeDiskBytes,
            CollectedAtUtc = DateTimeOffset.UtcNow
        });
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysicalMemory;
        public ulong AvailablePhysicalMemory;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;

        public MemoryStatusEx()
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        }
    }
}
