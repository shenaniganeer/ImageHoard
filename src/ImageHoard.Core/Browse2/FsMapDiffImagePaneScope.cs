using ImageHoard.Core.Browse;

namespace ImageHoard.Core.Browse2;

/// <summary>
/// Decides whether an <see cref="FsMapDiff"/> can affect the image list for a browse folder
/// (immediate children only vs recursive subtree).
/// </summary>
public static class FsMapDiffImagePaneScope
{
    /// <summary>
    /// Returns true when <paramref name="diff"/> may imply a change to image paths shown for
    /// <paramref name="normalizedCurrentFolder"/> (non-empty, normalized absolute path).
    /// </summary>
    public static bool TouchesImageList(string normalizedCurrentFolder, bool includeSubfolders, FsMapDiff diff)
    {
        if (string.IsNullOrEmpty(normalizedCurrentFolder))
            return false;

        var c = FavoriteIndexRoots.NormalizeFavoritePath(normalizedCurrentFolder);

        static bool AtOrUnder(string path, string root)
        {
            var p = FavoriteIndexRoots.NormalizeFavoritePath(path);
            return string.Equals(p, root, StringComparison.OrdinalIgnoreCase)
                   || FavoriteIndexRoots.IsStrictSubpath(p, root);
        }

        bool inScope(string path)
        {
            var p = FavoriteIndexRoots.NormalizeFavoritePath(path);
            return includeSubfolders
                ? AtOrUnder(p, c)
                : string.Equals(p, c, StringComparison.OrdinalIgnoreCase);
        }

        return diff switch
        {
            FsFolderRefreshedDiff r => inScope(r.Path),
            FsAggregatesUpdatedDiff a => inScope(a.Path),
            FsFolderRemovedDiff r => inScope(r.Path),
            FsFolderAddedDiff a => inScope(a.Path),
            FsFolderRenamedDiff r => inScope(r.OldPath) || inScope(r.NewPath),
            _ => false,
        };
    }
}
