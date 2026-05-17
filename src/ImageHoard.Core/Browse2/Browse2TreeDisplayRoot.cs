using System.IO;
using ImageHoard.Core.Browse;

namespace ImageHoard.Core.Browse2;

/// <summary>
/// Browse2 folder tree UI roots at the browse folder while <see cref="FsMapWorkspace.IndexRoot"/> may be a wider favorite-backed map.
/// </summary>
public static class Browse2TreeDisplayRoot
{
    /// <summary>
    /// Returns <paramref name="normalizedBrowseFolder"/> when it lies under <see cref="FsMapWorkspace.IndexRoot"/> and has a map row (or the nearest existing ancestor); otherwise the workspace index root.
    /// </summary>
    public static string ClampToWorkspace(FsMapWorkspace workspace, string? normalizedBrowseFolder)
    {
        var ir = FavoriteIndexRoots.NormalizeFavoritePath(workspace.IndexRoot);
        if (string.IsNullOrWhiteSpace(normalizedBrowseFolder))
            return ir;

        var b = FavoriteIndexRoots.NormalizeFavoritePath(normalizedBrowseFolder);
        if (!string.Equals(b, ir, StringComparison.OrdinalIgnoreCase)
            && !FavoriteIndexRoots.IsStrictSubpath(b, ir))
            return ir;

        if (workspace.TryGetEntry(b, out _))
            return b;

        var cur = b;
        while (!string.Equals(cur, ir, StringComparison.OrdinalIgnoreCase))
        {
            var p = Path.GetDirectoryName(cur);
            if (string.IsNullOrEmpty(p))
                return ir;
            cur = FavoriteIndexRoots.NormalizeFavoritePath(p);
            if (workspace.TryGetEntry(cur, out _))
                return cur;
        }

        return ir;
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="treeDisplayRoot"/> or a strict descendant of it.</summary>
    public static bool IsSameOrStrictDescendantOf(string treeDisplayRoot, string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        var dr = FavoriteIndexRoots.NormalizeFavoritePath(treeDisplayRoot);
        var p = FavoriteIndexRoots.NormalizeFavoritePath(path);
        return string.Equals(p, dr, StringComparison.OrdinalIgnoreCase)
            || FavoriteIndexRoots.IsStrictSubpath(p, dr);
    }
}
