using ImageHoard.Core.Browse;

namespace ImageHoard.Core.Browse2;

public static class FsMapPathHelpers
{
    public static string ParentPathOrEmpty(string fullPath, string indexRoot)
    {
        var n = FavoriteIndexRoots.NormalizeFavoritePath(fullPath);
        var r = FavoriteIndexRoots.NormalizeFavoritePath(indexRoot);
        if (string.Equals(n, r, StringComparison.OrdinalIgnoreCase))
            return "";
        var p = Path.GetDirectoryName(n);
        return string.IsNullOrEmpty(p) ? "" : FavoriteIndexRoots.NormalizeFavoritePath(p);
    }

    public static string DisplayName(string fullPath, string indexRoot)
    {
        var n = FavoriteIndexRoots.NormalizeFavoritePath(fullPath);
        var r = FavoriteIndexRoots.NormalizeFavoritePath(indexRoot);
        if (string.Equals(n, r, StringComparison.OrdinalIgnoreCase))
        {
            var trimmed = n.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fn = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(fn) ? n : fn;
        }

        var name = Path.GetFileName(n);
        return string.IsNullOrEmpty(name) ? n : name;
    }
}
