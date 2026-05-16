using ImageHoard.Core.Browse;

namespace ImageHoard.Core.Browse2;

/// <summary>
/// Resolves persisted browse folder paths against an on-disk <see cref="FsMapWorkspace"/> snapshot for cold-boot selection.
/// </summary>
public static class FsColdBootPathResolver
{
    /// <summary>
    /// Picks the deepest folder on <paramref name="candidate"/> → root walk that exists in the map; otherwise tries <paramref name="fallbackCandidate"/>.
    /// When nothing matches, returns <paramref name="indexRoot"/> normalized.
    /// </summary>
    public static string ResolveSelectedFolder(
        FsMapWorkspace workspace,
        string indexRoot,
        string? candidate,
        string? fallbackCandidate)
    {
        var root = FavoriteIndexRoots.NormalizeFavoritePath(indexRoot);
        if (TryClampToExistingMapFolder(workspace, root, candidate, out var a))
            return a;
        if (TryClampToExistingMapFolder(workspace, root, fallbackCandidate, out var b))
            return b;
        return root;
    }

    private static bool TryClampToExistingMapFolder(
        FsMapWorkspace workspace,
        string root,
        string? path,
        out string resolved)
    {
        resolved = root;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var n = FavoriteIndexRoots.NormalizeFavoritePath(path);
        if (!string.Equals(n, root, StringComparison.OrdinalIgnoreCase)
            && !FavoriteIndexRoots.IsStrictSubpath(n, root))
            return false;

        var cur = n;
        while (true)
        {
            if (workspace.TryGetEntry(cur, out _))
            {
                resolved = cur;
                return true;
            }

            if (string.Equals(cur, root, StringComparison.OrdinalIgnoreCase))
                break;
            var p = Path.GetDirectoryName(cur);
            if (string.IsNullOrEmpty(p))
                break;
            cur = FavoriteIndexRoots.NormalizeFavoritePath(p);
        }

        return false;
    }
}
