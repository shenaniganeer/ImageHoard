namespace ImageHoard.Core.Sort;

/// <summary>FR-SR-04 inverse-keep + FR-SR-03 unset block.</summary>
public static class BatchDeletePlanner
{
    /// <summary>Paths to send to Recycle Bin (not Keep), only when Unset == 0.</summary>
    public static bool TryGetDeletionSet(
        IReadOnlyList<string> imagePathsInScope,
        SortSession session,
        out IReadOnlyList<string>? pathsToDelete,
        out string? blockReason)
    {
        pathsToDelete = null;
        blockReason = null;
        var (_, _, unset) = session.CountStates(imagePathsInScope);
        if (unset > 0)
        {
            blockReason = "unset";
            return false;
        }

        var list = imagePathsInScope.Where(p => session.GetState(p) != SortFlagState.Keep).ToList();
        pathsToDelete = list;
        return true;
    }

    /// <summary>Paths explicitly marked Delete (FR-SR-04 scoped variant; no unset gate).</summary>
    public static IReadOnlyList<string> GetDeleteFlaggedPaths(
        IReadOnlyList<string> imagePathsInScope,
        SortSession session) =>
        imagePathsInScope.Where(p => session.GetState(p) == SortFlagState.Delete).ToList();

    /// <summary>
    /// Delete/archive wizard only: inverse-keep set (all non-<see cref="SortFlagState.Keep"/>), without FR-SR-03 unset blocking.
    /// </summary>
    public static IReadOnlyList<string> GetInverseKeepDeletionSetIgnoringUnsetGate(
        IReadOnlyList<string> imagePathsInScope,
        SortSession session) =>
        imagePathsInScope.Where(p => session.GetState(p) != SortFlagState.Keep).ToList();
}
