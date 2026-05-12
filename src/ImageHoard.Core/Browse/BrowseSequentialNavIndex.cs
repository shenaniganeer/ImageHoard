namespace ImageHoard.Core.Browse;

/// <summary>Discrete browse step for keyboard or command-driven image navigation.</summary>
public enum BrowseNavStepKind
{
    Next,
    Previous,
    First,
    Last,
}

/// <summary>
/// Resolves the current index in a visible image list for sequential next/prev, using a
/// synchronous anchor path first so rapid inputs are not lost to TreeView selection lag.
/// </summary>
public static class BrowseSequentialNavIndex
{
    /// <summary>
    /// Returns the index of the best-matching visible image, or -1 when <paramref name="visibleImagePaths"/> is empty
    /// or no hint matches.
    /// </summary>
    /// <remarks>
    /// Precedence: <paramref name="browseNavAnchorPath"/>, then <paramref name="treeSelectedImagePath"/>,
    /// then <paramref name="displayedImagePath"/>.
    /// </remarks>
    public static int ResolveCurrentIndex(
        IReadOnlyList<string> visibleImagePaths,
        string? browseNavAnchorPath,
        string? treeSelectedImagePath,
        string? displayedImagePath)
    {
        if (visibleImagePaths.Count == 0)
            return -1;

        var idx = TryFindPathIndex(visibleImagePaths, browseNavAnchorPath);
        if (idx >= 0)
            return idx;

        idx = TryFindPathIndex(visibleImagePaths, treeSelectedImagePath);
        if (idx >= 0)
            return idx;

        return TryFindPathIndex(visibleImagePaths, displayedImagePath);
    }

    /// <summary>
    /// Computes the visible list index after applying <paramref name="step"/>,
    /// using the same precedence as <see cref="ResolveCurrentIndex"/> for next/previous.
    /// </summary>
    /// <returns>-1 when <paramref name="visibleImagePaths"/> is empty; otherwise a clamped index in <c>[0, Count-1]</c>.</returns>
    public static int ComputeTargetIndexForStep(
        IReadOnlyList<string> visibleImagePaths,
        string? browseNavAnchorPath,
        string? treeSelectedImagePath,
        string? displayedImagePath,
        BrowseNavStepKind step)
    {
        if (visibleImagePaths.Count == 0)
            return -1;

        switch (step)
        {
            case BrowseNavStepKind.First:
                return 0;
            case BrowseNavStepKind.Last:
                return visibleImagePaths.Count - 1;
            case BrowseNavStepKind.Next:
            {
                var i = ResolveCurrentIndex(
                    visibleImagePaths,
                    browseNavAnchorPath,
                    treeSelectedImagePath,
                    displayedImagePath);
                if (i < 0)
                    return 0;
                return Math.Min(visibleImagePaths.Count - 1, i + 1);
            }
            case BrowseNavStepKind.Previous:
            {
                var i = ResolveCurrentIndex(
                    visibleImagePaths,
                    browseNavAnchorPath,
                    treeSelectedImagePath,
                    displayedImagePath);
                if (i <= 0)
                    return 0;
                return i - 1;
            }
            default:
                return -1;
        }
    }

    private static int TryFindPathIndex(IReadOnlyList<string> paths, string? path)
    {
        if (string.IsNullOrEmpty(path))
            return -1;

        for (var i = 0; i < paths.Count; i++)
        {
            if (string.Equals(paths[i], path, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }
}
