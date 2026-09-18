namespace WPO.Core.Security;

/// <summary>
/// Validates that a candidate filesystem path is safe to include in a cleanup
/// scan/preview/execution: it must resolve under an allowed root, must not
/// contain traversal segments, must not land in a denied system location, and
/// (by default) must not escape the allow-list via a symlink/junction.
/// </summary>
public interface IPathSafetyValidator
{
    PathValidationResult Validate(string candidatePath);
}
