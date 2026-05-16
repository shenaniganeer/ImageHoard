using ImageHoard.Core.Browse;

namespace ImageHoard.Core.Browse2;

/// <summary>Registry of <see cref="FsMapWorkspace"/> per browse root: dedupe-minimal favorites are persistent (on-disk); other roots are created transient in-memory on demand.</summary>
public sealed class FsMapRegistry
{
    private readonly Dictionary<string, FsMapWorkspace> _workspaces =
        new(StringComparer.OrdinalIgnoreCase);

    public FsMapRegistry(string mapsDirectory, IEnumerable<string> favorites, FsDiffStream diffStream)
    {
        MapsDirectory = mapsDirectory;
        DiffStream = diffStream;
        var roots = FavoriteIndexRoots.ComputeMinimalIndexRoots(favorites);
        IndexRoots = roots.Select(FavoriteIndexRoots.NormalizeFavoritePath).ToArray();
        foreach (var r in IndexRoots)
        {
            var file = FsMapPersistence.MapFilePathForIndexRoot(mapsDirectory, r);
            _workspaces[r] = new FsMapWorkspace(r, file, diffStream);
        }
    }

    public string MapsDirectory { get; }

    public FsDiffStream DiffStream { get; }

    public IReadOnlyList<string> IndexRoots { get; }

    public async Task LoadAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var ws in _workspaces.Values)
            await ws.LoadOrCreateEmptyAsync(cancellationToken).ConfigureAwait(false);
    }

    public FsMapWorkspace? TryGetWorkspace(string normalizedIndexRoot)
    {
        var key = FavoriteIndexRoots.NormalizeFavoritePath(normalizedIndexRoot);
        return _workspaces.GetValueOrDefault(key);
    }

    public FsMapWorkspace? TryGetWorkspaceForPath(string anyPath)
    {
        if (string.IsNullOrWhiteSpace(anyPath))
            return null;
        var norm = FavoriteIndexRoots.NormalizeFavoritePath(anyPath);
        var favoriteRoot = FavoriteIndexRoots.FindOwningIndexRoot(norm, IndexRoots);
        if (favoriteRoot != null && TryGetWorkspace(favoriteRoot) is { } favoriteWs)
            return favoriteWs;

        FsMapWorkspace? best = null;
        foreach (var kv in _workspaces)
        {
            var key = kv.Key;
            if (string.Equals(norm, key, StringComparison.OrdinalIgnoreCase)
                || FavoriteIndexRoots.IsStrictSubpath(norm, key))
            {
                if (best == null || key.Length > best.IndexRoot.Length)
                    best = kv.Value;
            }
        }

        return best;
    }

    /// <summary>
    /// True when <paramref name="normalizedIndexRoot"/> is a dedupe-minimal favorite-backed root (on-disk FsMap under <see cref="MapsDirectory"/>).
    /// </summary>
    public bool HasPersistentWorkspaceFor(string normalizedIndexRoot)
    {
        var key = FavoriteIndexRoots.NormalizeFavoritePath(normalizedIndexRoot);
        foreach (var r in IndexRoots)
        {
            if (string.Equals(r, key, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Ensures a transient (in-memory) workspace exists for a browse root that is not favorite-backed; no on-disk map until the path is added as a favorite.
    /// </summary>
    public FsMapWorkspace GetOrCreateWorkspaceForBrowseRoot(string rootPath)
    {
        var key = FavoriteIndexRoots.NormalizeFavoritePath(rootPath);
        if (_workspaces.TryGetValue(key, out var existing))
            return existing;

        var ws = new FsMapWorkspace(key, mapFilePath: "", DiffStream);
        _workspaces[key] = ws;
        return ws;
    }

    public IReadOnlyCollection<FsMapWorkspace> AllWorkspaces() => _workspaces.Values.ToArray();
}
