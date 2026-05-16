using ImageHoard.Core.Browse;

namespace ImageHoard.Core.Browse2;

/// <summary>Deep folder-name search over <see cref="FsMapWorkspace"/> paths (no live directory walk).</summary>
public static class BrowserFindDeepFolderMapQuery
{
    public static List<(string Path, string Name)> Search(
        FsMapWorkspace? workspace,
        string rootFolder,
        string trimmedQuery,
        bool matchFromStartOfName,
        CancellationToken cancellationToken = default)
    {
        var list = new List<(string Path, string Name)>();
        if (workspace == null || string.IsNullOrEmpty(trimmedQuery))
            return list;

        var normRoot = FavoriteIndexRoots.NormalizeFavoritePath(rootFolder);
        foreach (var path in workspace.CopyAllFolderPaths())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSameOrDescendantDirectory(normRoot, path))
                continue;
            if (string.Equals(path, normRoot, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!workspace.TryGetEntry(path, out var e))
                continue;
            var name = string.IsNullOrEmpty(e.Name) ? Path.GetFileName(path) : e.Name;
            if (!BrowserFindNameMatching.NameMatches(trimmedQuery, name, matchFromStartOfName))
                continue;
            list.Add((path, name));
        }

        list.Sort(static (a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    private static bool IsSameOrDescendantDirectory(string root, string candidate)
    {
        var r = FavoriteIndexRoots.NormalizeFavoritePath(root);
        var c = FavoriteIndexRoots.NormalizeFavoritePath(candidate);
        return string.Equals(c, r, StringComparison.OrdinalIgnoreCase)
               || FavoriteIndexRoots.IsStrictSubpath(c, r);
    }
}
