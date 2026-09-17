using WPO.Core.Security;

namespace WPO.Core.Tests.Security;

public class PathSafetyValidatorTests
{
    private static readonly string AllowedRoot = @"C:\Temp\WpoAllowed\Cache";

    private static PathSafetyValidator CreateValidator(PathSafetyOptions? options = null)
    {
        options ??= new PathSafetyOptions
        {
            AllowedRoots = { AllowedRoot }
        };
        return new PathSafetyValidator(options);
    }

    [Fact]
    public void Validate_PathUnderAllowedRoot_IsAllowed()
    {
        var validator = CreateValidator();

        var result = validator.Validate(Path.Combine(AllowedRoot, "sub", "file.tmp"));

        Assert.True(result.IsAllowed);
        Assert.Null(result.RejectionReason);
    }

    [Fact]
    public void Validate_PathEqualToAllowedRoot_IsAllowed()
    {
        var validator = CreateValidator();

        var result = validator.Validate(AllowedRoot);

        Assert.True(result.IsAllowed);
    }

    [Theory]
    [InlineData(@"C:\Temp\WpoAllowed\Cache\..\..\Windows\System32\evil.dll")]
    [InlineData(@"..\..\Windows\System32")]
    public void Validate_PathContainingTraversalSegment_IsRejected(string candidate)
    {
        var validator = CreateValidator();

        var result = validator.Validate(candidate);

        Assert.False(result.IsAllowed);
        Assert.Equal("TraversalSegment", result.RejectionReason);
    }

    [Fact]
    public void Validate_SiblingFolderWithSimilarPrefix_IsRejected()
    {
        // "CacheOld" starts with the same characters as the allowed root "Cache"
        // but is a completely different, sibling directory - a naive
        // string.StartsWith check would incorrectly allow this.
        var validator = CreateValidator();
        var similarPrefixPath = AllowedRoot + "Old" + Path.DirectorySeparatorChar + "file.tmp";

        var result = validator.Validate(similarPrefixPath);

        Assert.False(result.IsAllowed);
        Assert.Equal("NotInAllowList", result.RejectionReason);
    }

    [Fact]
    public void Validate_PathNotUnderAnyAllowedRoot_IsRejected()
    {
        var validator = CreateValidator();

        var result = validator.Validate(@"C:\SomeOther\Path\file.tmp");

        Assert.False(result.IsAllowed);
        Assert.Equal("NotInAllowList", result.RejectionReason);
    }

    [Fact]
    public void Validate_PathUnderDeniedRoot_IsRejectedEvenIfWhitelisted()
    {
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var options = new PathSafetyOptions
        {
            // Misconfiguration: an operator accidentally allow-listed the Windows directory too.
            AllowedRoots = { AllowedRoot, windowsDir }
        };
        var validator = new PathSafetyValidator(options);

        var result = validator.Validate(Path.Combine(windowsDir, "System32", "evil.dll"));

        Assert.False(result.IsAllowed);
        Assert.Equal("DeniedRoot", result.RejectionReason);
    }

    [Theory]
    [InlineData(@"C:\")]
    public void Validate_DriveRoot_IsRejected(string candidate)
    {
        var options = new PathSafetyOptions
        {
            AllowedRoots = { @"C:\" }
        };
        var validator = new PathSafetyValidator(options);

        var result = validator.Validate(candidate);

        Assert.False(result.IsAllowed);
        Assert.Equal("DriveRootDeletionForbidden", result.RejectionReason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyOrWhitespacePath_IsRejected(string? candidate)
    {
        var validator = CreateValidator();

        var result = validator.Validate(candidate!);

        Assert.False(result.IsAllowed);
        Assert.Equal("EmptyPath", result.RejectionReason);
    }
}

