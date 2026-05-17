using ImageHoard.Core.Browse;

namespace ImageHoard.Core.Browse2;

/// <summary>In-memory + disk slice for one index root.</summary>
public sealed class FsMapWorkspace
{
    private readonly object _lock = new();
    private readonly FsDiffStream? _diffStream;
    private Dictionary<string, FsMapEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public FsMapWorkspace(string indexRoot, string mapFilePath, FsDiffStream? diffStream)
    {
        IndexRoot = FavoriteIndexRoots.NormalizeFavoritePath(indexRoot);
        MapFilePath = mapFilePath;
        _diffStream = diffStream;
    }

    public string IndexRoot { get; }

    public string MapFilePath { get; }

    /// <summary>When false, the workspace is transient in-memory only (empty <see cref="MapFilePath"/>); no disk load/save. Favorites use persistent workspaces.</summary>
    public bool IsPersistent => !string.IsNullOrWhiteSpace(MapFilePath);

    public async Task LoadOrCreateEmptyAsync(CancellationToken ct = default)
    {
        if (!IsPersistent)
        {
            lock (_lock)
            {
                _entries = new Dictionary<string, FsMapEntry>(StringComparer.OrdinalIgnoreCase);
                EnsureIndexRootPlaceholderLocked();
            }

            return;
        }

        var loaded = await FsMapPersistence.TryLoadAsync(MapFilePath, ct).ConfigureAwait(false);
        lock (_lock)
        {
            if (loaded != null
                && string.Equals(
                    FavoriteIndexRoots.NormalizeFavoritePath(loaded.IndexRoot),
                    IndexRoot,
                    StringComparison.OrdinalIgnoreCase)
                && loaded.Entries != null)
            {
                _entries = new Dictionary<string, FsMapEntry>(loaded.Entries, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                _entries = new Dictionary<string, FsMapEntry>(StringComparer.OrdinalIgnoreCase);
            }

            EnsureIndexRootPlaceholderLocked();
        }
    }

    /// <summary>
    /// Guarantees a row exists for <see cref="IndexRoot"/> so <see cref="FolderTreeFlatModel.Rebuild"/> can render the root
    /// before any live refresh or background scan (no diff raised — load-only).
    /// </summary>
    private void EnsureIndexRootPlaceholderLocked()
    {
        var key = IndexRoot;
        if (_entries.ContainsKey(key))
            return;

        _entries[key] = new FsMapEntry
        {
            ParentPath = "",
            Name = FsMapPathHelpers.DisplayName(key, key),
            DirectoryMtimeUtc = null,
            HasSubfolders = true,
            AggregateSizeBytes = 0,
            TotalFileCount = 0,
            ImageFileCount = 0,
            LastVerifiedAtUtc = null,
        };
    }

    public bool TryGetEntry(string path, out FsMapEntry entry)
    {
        var key = FavoriteIndexRoots.NormalizeFavoritePath(path);
        lock (_lock)
            return _entries.TryGetValue(key, out entry!);
    }

    public int EntryCount
    {
        get
        {
            lock (_lock)
                return _entries.Count;
        }
    }

    public IReadOnlyList<string> GetDirectChildPaths(string parentPath)
    {
        var parent = FavoriteIndexRoots.NormalizeFavoritePath(parentPath);
        lock (_lock)
        {
            var list = new List<string>();
            foreach (var kv in _entries)
            {
                if (string.Equals(kv.Value.ParentPath, parent, StringComparison.OrdinalIgnoreCase))
                    list.Add(kv.Key);
            }

            return list;
        }
    }

    /// <summary>Snapshot of all folder paths in the map (for find / diagnostics; copy under lock).</summary>
    public List<string> CopyAllFolderPaths()
    {
        lock (_lock)
            return new List<string>(_entries.Keys);
    }

    private FsMapDocument BuildDocumentLocked()
    {
        return new FsMapDocument
        {
            FormatVersion = 1,
            IndexRoot = IndexRoot,
            SavedAtUtc = DateTimeOffset.UtcNow,
            Entries = new Dictionary<string, FsMapEntry>(_entries, StringComparer.OrdinalIgnoreCase),
        };
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        if (!IsPersistent)
            return;

        FsMapDocument doc;
        lock (_lock)
            doc = BuildDocumentLocked();
        await FsMapPersistence.SaveAsync(MapFilePath, doc, ct).ConfigureAwait(false);
    }

    private void Raise(IReadOnlyList<FsMapDiff> diffs)
    {
        if (_diffStream == null || diffs.Count == 0)
            return;
        _diffStream.RaiseMany(diffs);
    }

    /// <summary>
    /// Raises a single <see cref="FsAggregatesUpdatedDiff"/> for <paramref name="path"/> from the current in-memory row
    /// (Browse2 flat-model resort after targeted aggregate refresh; complements <see cref="UpsertDirectoryRow"/> which emits refreshed diffs).
    /// </summary>
    public void EmitAggregatesUpdatedForPath(string path)
    {
        var key = FavoriteIndexRoots.NormalizeFavoritePath(path);
        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out var e))
                return;
            Raise(new List<FsMapDiff>
            {
                new FsAggregatesUpdatedDiff(IndexRoot, key, e.AggregateSizeBytes, e.TotalFileCount, e.ImageFileCount),
            });
        }
    }

    private static List<string> CollectSubtreeKeysLocked(Dictionary<string, FsMapEntry> entries, string subtreeRoot)
    {
        var root = FavoriteIndexRoots.NormalizeFavoritePath(subtreeRoot);
        var keys = new List<string>();
        foreach (var k in entries.Keys)
        {
            if (string.Equals(k, root, StringComparison.OrdinalIgnoreCase)
                || FavoriteIndexRoots.IsStrictSubpath(k, root))
                keys.Add(k);
        }

        keys.Sort((a, b) => b.Length.CompareTo(a.Length));
        return keys;
    }

    /// <summary>Removes every key under <paramref name="subtreeRoot"/> (inclusive) and returns diffs (deepest first).</summary>
    public IReadOnlyList<FsMapDiff> RemoveSubtree(string subtreeRoot)
    {
        var root = FavoriteIndexRoots.NormalizeFavoritePath(subtreeRoot);
        var diffs = new List<FsMapDiff>();
        lock (_lock)
        {
            var toRemove = CollectSubtreeKeysLocked(_entries, root);
            foreach (var k in toRemove)
            {
                if (!_entries.TryGetValue(k, out var e))
                    continue;
                var parent = e.ParentPath;
                _entries.Remove(k);
                diffs.Add(new FsFolderRemovedDiff(IndexRoot, k, parent));
            }
        }

        Raise(diffs);
        return diffs;
    }

    /// <summary>Invalidates mtime trust for immediate child directories of <paramref name="parentPath"/>.</summary>
    public IReadOnlyList<FsMapDiff> InvalidateImmediateChildTrust(string parentPath)
    {
        var parent = FavoriteIndexRoots.NormalizeFavoritePath(parentPath);
        var diffs = new List<FsMapDiff>();
        lock (_lock)
        {
            foreach (var kv in _entries.ToArray())
            {
                var p = kv.Key;
                var e = kv.Value;
                if (!string.Equals(e.ParentPath, parent, StringComparison.OrdinalIgnoreCase))
                    continue;
                var before = FsMapEntryClone.Snapshot(e);
                e.DirectoryMtimeUtc = null;
                e.LastVerifiedAtUtc = null;
                diffs.Add(new FsFolderRefreshedDiff(IndexRoot, p, before, FsMapEntryClone.Snapshot(e)));
            }
        }

        Raise(diffs);
        return diffs;
    }

    /// <summary>Updates directory mtime only (preserves aggregates / structure fields).</summary>
    public IReadOnlyList<FsMapDiff> PatchDirectoryMtime(string path, DateTimeOffset? directoryMtimeUtc)
    {
        var key = FavoriteIndexRoots.NormalizeFavoritePath(path);
        var diffs = new List<FsMapDiff>();
        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out var row))
                return diffs;
            var before = FsMapEntryClone.Snapshot(row);
            row.DirectoryMtimeUtc = directoryMtimeUtc;
            var after = FsMapEntryClone.Snapshot(row);
            diffs.Add(new FsFolderRefreshedDiff(IndexRoot, key, before, after));
        }

        Raise(diffs);
        return diffs;
    }

    /// <summary>Upserts a directory row and raises <see cref="FsFolderRefreshedDiff"/> (or add if new).</summary>
    public IReadOnlyList<FsMapDiff> UpsertDirectoryRow(
        string path,
        string parentPath,
        string name,
        DateTimeOffset? directoryMtimeUtc,
        bool hasSubfolders,
        long aggregateSizeBytes,
        int totalFileCount,
        int imageFileCount,
        DateTimeOffset? lastVerifiedAtUtc)
    {
        var key = FavoriteIndexRoots.NormalizeFavoritePath(path);
        var parent = FavoriteIndexRoots.NormalizeFavoritePath(parentPath);
        var diffs = new List<FsMapDiff>();
        lock (_lock)
        {
            var isNew = !_entries.ContainsKey(key);
            _entries.TryGetValue(key, out var existing);
            var beforeSnap = existing == null ? null : FsMapEntryClone.Snapshot(existing);
            var row = existing ?? new FsMapEntry();
            row.ParentPath = parent;
            row.Name = name;
            row.DirectoryMtimeUtc = directoryMtimeUtc;
            row.HasSubfolders = hasSubfolders;
            row.AggregateSizeBytes = aggregateSizeBytes;
            row.TotalFileCount = totalFileCount;
            row.ImageFileCount = imageFileCount;
            row.LastVerifiedAtUtc = lastVerifiedAtUtc;
            _entries[key] = row;
            var afterSnap = FsMapEntryClone.Snapshot(row);
            if (isNew)
                diffs.Add(new FsFolderAddedDiff(IndexRoot, key, parent, afterSnap));
            else
                diffs.Add(new FsFolderRefreshedDiff(IndexRoot, key, beforeSnap, afterSnap));
        }

        Raise(diffs);
        return diffs;
    }

    /// <summary>Remaps every entry whose key is <paramref name="sourcePrefix"/> or a strict child onto <paramref name="destinationPrefix"/>.</summary>
    public IReadOnlyList<FsMapDiff> RemapSubtreePrefix(string sourcePrefix, string destinationPrefix)
    {
        var src = FavoriteIndexRoots.NormalizeFavoritePath(sourcePrefix);
        var dst = FavoriteIndexRoots.NormalizeFavoritePath(destinationPrefix);
        var diffs = new List<FsMapDiff>();
        if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase))
            return diffs;

        lock (_lock)
        {
            var keys = CollectSubtreeKeysLocked(_entries, src);
            var pairs = new List<(string OldPath, string NewPath, FsMapEntry Entry)>();
            foreach (var k in keys)
            {
                if (!_entries.TryGetValue(k, out var e))
                    continue;
                _entries.Remove(k);
                var rel = RelativeChildPath(src, k);
                var nk = string.IsNullOrEmpty(rel)
                    ? dst
                    : FavoriteIndexRoots.NormalizeFavoritePath(Path.Combine(dst, rel));
                e.Name = Path.GetFileName(nk);
                e.ParentPath = FsMapPathHelpers.ParentPathOrEmpty(nk, IndexRoot);
                pairs.Add((k, nk, e));
            }

            foreach (var (_, nk, e) in pairs)
                _entries[nk] = e;

            foreach (var (oldPath, newPath, _) in pairs.OrderByDescending(p => p.NewPath.Length))
            {
                var oldParent = FsMapPathHelpers.ParentPathOrEmpty(oldPath, IndexRoot);
                var newParent = FsMapPathHelpers.ParentPathOrEmpty(newPath, IndexRoot);
                diffs.Add(new FsFolderRenamedDiff(IndexRoot, oldPath, newPath, oldParent, newParent));
            }
        }

        Raise(diffs);
        return diffs;
    }

    /// <summary>
    /// Decrements subtree aggregate counters along the ancestor directory chain for each removed file (wizard post-delete).
    /// Emits <see cref="FsAggregatesUpdatedDiff"/> for touched directory rows that already exist in the map.
    /// </summary>
    public IReadOnlyList<FsMapDiff> ApplyWizardRemovedImageFileStats(
        IReadOnlyList<(string FullPath, long LengthBytes, bool IsImage)> removals)
    {
        if (removals.Count == 0)
            return Array.Empty<FsMapDiff>();

        List<FsMapDiff> diffs;
        lock (_lock)
        {
            var root = IndexRoot;
            var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (fullPath, lengthBytes, isImage) in removals)
            {
                var normFile = FavoriteIndexRoots.NormalizeFavoritePath(fullPath);
                var parent = Path.GetDirectoryName(normFile);
                while (!string.IsNullOrEmpty(parent))
                {
                    var n = FavoriteIndexRoots.NormalizeFavoritePath(parent);
                    if (!string.Equals(n, root, StringComparison.OrdinalIgnoreCase)
                        && !FavoriteIndexRoots.IsStrictSubpath(n, root))
                        break;

                    if (_entries.TryGetValue(n, out var e))
                    {
                        e.AggregateSizeBytes = Math.Max(0, e.AggregateSizeBytes - lengthBytes);
                        e.TotalFileCount = Math.Max(0, e.TotalFileCount - 1);
                        if (isImage)
                            e.ImageFileCount = Math.Max(0, e.ImageFileCount - 1);
                        touched.Add(n);
                    }

                    if (string.Equals(n, root, StringComparison.OrdinalIgnoreCase))
                        break;
                    parent = Directory.GetParent(n)?.FullName;
                }
            }

            diffs = new List<FsMapDiff>();
            foreach (var p in touched.OrderBy(x => x.Length))
            {
                if (!_entries.TryGetValue(p, out var e2))
                    continue;
                diffs.Add(new FsAggregatesUpdatedDiff(
                    IndexRoot,
                    p,
                    e2.AggregateSizeBytes,
                    e2.TotalFileCount,
                    e2.ImageFileCount));
            }
        }

        Raise(diffs);
        return diffs;
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
}
