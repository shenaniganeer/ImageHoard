using ImageHoard.Core.Browse;

namespace ImageHoard.Core.Browse2;

/// <summary>Which folders are expanded in the flat projection (capped; oldest expanded is dropped first).</summary>
public sealed class ExpansionState
{
    private readonly HashSet<string> _set = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _insertionOrder = new();

    public IReadOnlyList<string> ExpandedPaths => _insertionOrder;

    public bool Contains(string path)
    {
        var p = FavoriteIndexRoots.NormalizeFavoritePath(path);
        return _set.Contains(p);
    }

    /// <summary>Returns true if the expanded set changed (added, or evicted another path to stay under the cap).</summary>
    public bool TryExpand(string path)
    {
        var p = FavoriteIndexRoots.NormalizeFavoritePath(path);
        if (_set.Contains(p))
            return false;

        _set.Add(p);
        _insertionOrder.Add(p);
        EvictOverflow();
        return true;
    }

    public bool TryCollapse(string path)
    {
        var p = FavoriteIndexRoots.NormalizeFavoritePath(path);
        if (!_set.Remove(p))
            return false;

        for (var i = 0; i < _insertionOrder.Count; i++)
        {
            if (!string.Equals(_insertionOrder[i], p, StringComparison.OrdinalIgnoreCase))
                continue;
            _insertionOrder.RemoveAt(i);
            break;
        }

        return true;
    }

    public void Clear()
    {
        _set.Clear();
        _insertionOrder.Clear();
    }

    /// <summary>Replaces expansion with normalized paths in order (last paths win if over cap).</summary>
    public void Load(IEnumerable<string> paths)
    {
        Clear();
        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var p = FavoriteIndexRoots.NormalizeFavoritePath(raw);
            if (_set.Add(p))
                _insertionOrder.Add(p);
            EvictOverflow();
        }
    }

    public void RemoveSubtreeKeys(string subtreeRoot)
    {
        var root = FavoriteIndexRoots.NormalizeFavoritePath(subtreeRoot);
        var toRemove = new List<string>();
        foreach (var p in _insertionOrder)
        {
            if (string.Equals(p, root, StringComparison.OrdinalIgnoreCase)
                || FavoriteIndexRoots.IsStrictSubpath(p, root))
                toRemove.Add(p);
        }

        foreach (var p in toRemove)
        {
            _set.Remove(p);
            _insertionOrder.Remove(p);
        }
    }

    /// <summary>Rewrites expanded paths under <paramref name="oldPrefix"/> onto <paramref name="newPrefix"/>.</summary>
    public void RemapSubtreePrefix(string oldPrefix, string newPrefix)
    {
        var src = FavoriteIndexRoots.NormalizeFavoritePath(oldPrefix);
        var dst = FavoriteIndexRoots.NormalizeFavoritePath(newPrefix);
        if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase))
            return;

        var next = new List<string>(_insertionOrder.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in _insertionOrder)
        {
            var q = RemapOnePath(p, src, dst);
            if (!seen.Add(q))
                continue;
            next.Add(q);
        }

        while (next.Count > BrowserTreeSnapshot.MaxExpandedFolderPaths)
            next.RemoveAt(0);

        _insertionOrder.Clear();
        _insertionOrder.AddRange(next);
        _set.Clear();
        foreach (var x in next)
            _set.Add(x);
    }

    private static string RemapOnePath(string path, string oldPrefix, string newPrefix)
    {
        if (string.Equals(path, oldPrefix, StringComparison.OrdinalIgnoreCase))
            return newPrefix;
        if (!FavoriteIndexRoots.IsStrictSubpath(path, oldPrefix))
            return path;
        var rel = RelativeChildPath(oldPrefix, path);
        return string.IsNullOrEmpty(rel)
            ? newPrefix
            : FavoriteIndexRoots.NormalizeFavoritePath(Path.Combine(newPrefix, rel));
    }

    private static string RelativeChildPath(string root, string fullPath)
    {
        var r = FavoriteIndexRoots.NormalizeFavoritePath(root);
        var p = FavoriteIndexRoots.NormalizeFavoritePath(fullPath);
        if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase))
            return "";
        if (!p.StartsWith(r, StringComparison.OrdinalIgnoreCase))
            return "";
        if (p.Length <= r.Length)
            return "";
        var c = p[r.Length];
        if (c is not ('\\' or '/'))
            return "";
        return p[(r.Length + 1)..];
    }

    private void EvictOverflow()
    {
        while (_insertionOrder.Count > BrowserTreeSnapshot.MaxExpandedFolderPaths)
        {
            var victim = _insertionOrder[0];
            _insertionOrder.RemoveAt(0);
            _set.Remove(victim);
        }
    }
}
