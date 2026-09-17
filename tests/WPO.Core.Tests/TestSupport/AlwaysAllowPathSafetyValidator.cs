using WPO.Core.Security;

namespace WPO.Core.Tests.TestSupport;

/// <summary>
/// Path safety validator stub that allows any path unmodified. Used in
/// execution-focused tests where path safety itself is not under test.
/// </summary>
public sealed class AlwaysAllowPathSafetyValidator : IPathSafetyValidator
{
    public PathValidationResult Validate(string candidatePath) => PathValidationResult.Allow(candidatePath);
}
