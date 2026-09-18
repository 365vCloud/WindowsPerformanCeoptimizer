using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.VisualBasic.FileIO;

namespace WPO.Core.RecycleBin;

/// <summary>
/// Real, Windows-only Recycle Bin implementation backed by the Windows Shell
/// (via <see cref="Microsoft.VisualBasic.FileIO.FileSystem"/>). Permanent
/// deletion is a separate, explicit method and is never invoked by the
/// cleanup services unless the caller has already gathered an explicit
/// second confirmation from the user.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRecycleBinService : IRecycleBinService
{
    public Task<RecycleOperationResult> MoveToRecycleBinAsync(string fullPath, CancellationToken cancellationToken)
    {
        return Task.Run(() => Execute(fullPath, permanent: false), cancellationToken);
    }

    public Task<RecycleOperationResult> DeletePermanentlyAsync(string fullPath, CancellationToken cancellationToken)
    {
        return Task.Run(() => Execute(fullPath, permanent: true), cancellationToken);
    }

    private static RecycleOperationResult Execute(string fullPath, bool permanent)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RecycleOperationResult.Failure("RecycleBin operations require Windows.");
        }

        try
        {
            if (Directory.Exists(fullPath))
            {
                FileSystem.DeleteDirectory(
                    fullPath,
                    UIOption.OnlyErrorDialogs,
                    permanent ? RecycleOption.DeletePermanently : RecycleOption.SendToRecycleBin,
                    UICancelOption.ThrowException);
            }
            else if (File.Exists(fullPath))
            {
                FileSystem.DeleteFile(
                    fullPath,
                    UIOption.OnlyErrorDialogs,
                    permanent ? RecycleOption.DeletePermanently : RecycleOption.SendToRecycleBin,
                    UICancelOption.ThrowException);
            }
            else
            {
                return RecycleOperationResult.Failure("Path no longer exists.");
            }

            return RecycleOperationResult.Success();
        }
        catch (Exception ex)
        {
            return RecycleOperationResult.Failure(ex.Message);
        }
    }
}
