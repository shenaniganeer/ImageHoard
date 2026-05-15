namespace ImageHoard.Core.Browse;

/// <summary>
/// Derives minimal persisted-map roots from the favorites list so nested favorites share one on-disk map.
/// </summary>
public static class FavoriteIndexRoots
{
    /// <summary>Returns favorites that are not strict descendants of another favorite (directory-boundary prefix).</summary>
    public static IReadOnlyList<string> ComputeMinimalIndexRoots(IEnumerable<string> favorites)
    {
        var norm = favorites
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(NormalizeFavoritePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s.Length)
            .ThenBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (norm.Count == 0)
            return Array.Empty<string>();

        var roots = new List<string>();
        foreach (var p in norm)
        {
            var dominated = false;
            foreach (var r in roots)
            {
                if (IsStrictSubpath(p, r))
                {
                    dominated = true;
                    break;
                }
            }

            if (!dominated)
                roots.Add(p);
        }

        return roots;
    }

    /// <summary>True if <paramref name="path"/> is a proper subdirectory of <paramref name="ancestor"/>.</summary>
    public static bool IsStrictSubpath(string path, string ancestor)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(ancestor))
            return false;
        var p = NormalizeFavoritePath(path);
        var a = NormalizeFavoritePath(ancestor);
        if (p.Length <= a.Length)
            return false;
        if (!p.StartsWith(a, StringComparison.OrdinalIgnoreCase))
            return false;
        if (p.Length == a.Length)
            return false;
        var c = p[a.Length];
        return c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
    }

    /// <summary>Which index root owns <paramref name="path"/> (null if none).</summary>
    public static string? FindOwningIndexRoot(string path, IReadOnlyList<string> indexRoots)
    {
        if (string.IsNullOrEmpty(path) || indexRoots.Count == 0)
            return null;
        var p = NormalizeFavoritePath(path);
        string? best = null;
        foreach (var r in indexRoots)
        {
            if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase))
                return r;
            if (IsStrictSubpath(p, r) && (best == null || r.Length > best.Length))
                best = r;
        }

        return best;
    }

    public static string NormalizeFavoritePath(string path)
    {
        var t = path.Trim();
        if (string.IsNullOrEmpty(t))
            return t;
        try
        {
            t = Path.GetFullPath(t.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch
        {
            t = t.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return t;
    }
}
