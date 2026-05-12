namespace ImageHoard.Core.Services;

/// <summary>
/// Constants from <c>docs/design-decisions/symlink-junction-policy.md</c>.
/// </summary>
public static class SymlinkTraversalPolicy
{
    public const int MaxSymlinkDepthFromRoot = 4;
}
