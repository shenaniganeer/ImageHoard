using ImageHoard.Core.Browse;

namespace ImageHoard.Core.Browse2;

/// <summary>
/// Pure scroll-anchor math shared by the WinUI tree and unit tests (top visible row path + intra-row offset).
/// </summary>
public static class FolderTreeViewportAnchorMath
{
    /// <summary>Captures the folder path at the top of the viewport and the pixel offset within that row.</summary>
    public static (string AnchorPath, double OffsetInRowPx)? TryCaptureTopVisibleRow(
        double verticalOffset,
        double rowHeight,
        int rowCount,
        Func<int, string> getPathAtIndex)
    {
        if (rowCount <= 0 || rowHeight <= 0)
            return null;

        var firstIdx = (int)Math.Floor(verticalOffset / rowHeight);
        if (firstIdx < 0)
            firstIdx = 0;
        if (firstIdx >= rowCount)
            firstIdx = rowCount - 1;

        var anchorPath = FavoriteIndexRoots.NormalizeFavoritePath(getPathAtIndex(firstIdx));
        var offsetInRow = verticalOffset - firstIdx * rowHeight;
        if (offsetInRow < 0 || double.IsNaN(offsetInRow) || double.IsInfinity(offsetInRow))
            offsetInRow = 0;
        if (offsetInRow > rowHeight)
            offsetInRow = rowHeight;

        return (anchorPath, offsetInRow);
    }

    /// <summary>
    /// Resolves <paramref name="anchorFolderPath"/> to a visible row index, walking to ancestors when the path
    /// is missing (deleted row or anchor inside a collapsed subtree).
    /// </summary>
    public static int ResolveRowIndexForAnchor(
        string anchorFolderPath,
        string? indexRoot,
        int rowCount,
        Func<int, string> getPathAtIndex)
    {
        if (rowCount <= 0)
            return 0;

        int FindIndex(string path)
        {
            var n = FavoriteIndexRoots.NormalizeFavoritePath(path);
            for (var i = 0; i < rowCount; i++)
            {
                if (string.Equals(getPathAtIndex(i), n, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        var norm = FavoriteIndexRoots.NormalizeFavoritePath(anchorFolderPath);
        var ix = FindIndex(norm);
        if (ix >= 0)
            return ix;

        for (var p = ParentPathOrEmpty(norm, indexRoot); !string.IsNullOrEmpty(p); p = ParentPathOrEmpty(p, indexRoot))
        {
            ix = FindIndex(p);
            if (ix >= 0)
                return ix;
        }

        return 0;
    }

    public static double ComputeRestoredVerticalOffset(
        string anchorFolderPath,
        double offsetInRowPx,
        double rowHeight,
        string? indexRoot,
        int rowCount,
        Func<int, string> getPathAtIndex,
        double maxScrollOffset)
    {
        if (rowHeight <= 0 || rowCount <= 0)
            return 0;

        var off = offsetInRowPx;
        if (double.IsNaN(off) || double.IsInfinity(off) || off < 0)
            off = 0;
        if (off > rowHeight)
            off = rowHeight;

        var ix = ResolveRowIndexForAnchor(anchorFolderPath, indexRoot, rowCount, getPathAtIndex);
        var y = ix * rowHeight + off;
        return ClampVerticalScrollTarget(y, maxScrollOffset);
    }

    public static double ClampVerticalScrollTarget(double y, double maxScrollOffset)
    {
        if (double.IsNaN(y) || double.IsInfinity(y))
            return 0;
        var max = maxScrollOffset < 0 ? 0 : maxScrollOffset;
        if (y < 0)
            return 0;
        return y > max ? max : y;
    }

    private static string ParentPathOrEmpty(string fullPath, string? indexRoot)
    {
        var n = FavoriteIndexRoots.NormalizeFavoritePath(fullPath);
        var r = string.IsNullOrEmpty(indexRoot) ? null : FavoriteIndexRoots.NormalizeFavoritePath(indexRoot);
        if (r is not null && string.Equals(n, r, StringComparison.OrdinalIgnoreCase))
            return "";
        var p = Path.GetDirectoryName(n);
        return string.IsNullOrEmpty(p) ? "" : FavoriteIndexRoots.NormalizeFavoritePath(p);
    }
}
