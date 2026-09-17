using WPO.Core.Audit;

namespace WPO.Core.Tests.Audit;

public class AuditLogMaskerTests
{
    private const string UserProfileRoot = @"C:\Users\Alice";
    private const string UserName = "Alice";

    private static AuditLogMasker CreateMasker() => new(UserProfileRoot, UserName);

    [Fact]
    public void Mask_PathUnderUserProfile_ReplacesProfileRootWithPlaceholder()
    {
        var masker = CreateMasker();

        var masked = masker.Mask(@"C:\Users\Alice\AppData\Local\Temp\file.tmp");

        Assert.Equal(@"<user>\AppData\Local\Temp\file.tmp", masked);
        Assert.DoesNotContain("Alice", masked);
    }

    [Fact]
    public void Mask_PathEqualToUserProfileRoot_ReturnsPlaceholder()
    {
        var masker = CreateMasker();

        var masked = masker.Mask(UserProfileRoot);

        Assert.Equal("<user>", masked);
    }

    [Fact]
    public void Mask_UserNameAppearingElsewhereInPath_IsReplaced()
    {
        var masker = CreateMasker();

        var masked = masker.Mask(@"D:\Backups\Alice\file.tmp");

        Assert.DoesNotContain("Alice", masked);
        Assert.Equal(@"D:\Backups\<user>\file.tmp", masked);
    }

    [Fact]
    public void Mask_PathWithoutUserInfo_IsUnchanged()
    {
        var masker = CreateMasker();

        var masked = masker.Mask(@"C:\Temp\Cache\file.tmp");

        Assert.Equal(@"C:\Temp\Cache\file.tmp", masked);
    }
}
