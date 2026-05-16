using ImageHoard.Core.Browse;

namespace ImageHoard.Core.Browse2;

/// <summary>
/// Visible-line folder tree projection for one <see cref="FsMapWorkspace"/> index root.
/// Applies expand/collapse splices and reacts to <see cref="FsMapDiff"/> from <see cref="FsDiffStream"/>.
/// </summary>
public sealed class FolderTreeFlatModel : IDisposable
{
    private readonly FsMapWorkspace _workspace;
    private readonly FsDiffStream _diffStream;
    private readonly bool _ownsSubscription;
    private readonly List<FolderRow> _rows = new();
    private readonly Dictionary<string, int> _rowIndexByPath = new(StringComparer.OrdinalIgnoreCase);
    private FolderListSortKind _folderSortKind = FolderListSortKind.NameNatural;

    public FolderTreeFlatModel(FsMapWorkspace workspace, FsDiffStream diffStream, bool subscribeToDiffStream = true)
    {
        _workspace = workspace;
        _diffStream = diffStream;
        if (subscribeToDiffStream)
        {
            _diffStream.DiffReceived += OnDiffReceived;
            _ownsSubscription = true;
        }
    }

    public ExpansionState Expansion { get; } = new();

    public SelectionState Selection { get; } = new();

    public IReadOnlyList<FolderRow> Rows => _rows;

    public IReadOnlyDictionary<string, int> RowIndexByPath => _rowIndexByPath;

    public FolderListSortKind FolderSortKind => _folderSortKind;

    /// <summary>Sets sibling sort for visible rows and rebuilds the projection.</summary>
    public FlatModelDelta SetFolderSortKind(FolderListSortKind kind)
    {
        if (_folderSortKind == kind)
            return FlatModelDelta.Empty;
        _folderSortKind = kind;
        return Rebuild();
    }

    /// <summary>Assigns sort kind before the first <see cref="Rebuild"/> (cold boot) without emitting a delta.</summary>
    public void InitializeFolderSortKind(FolderListSortKind kind) => _folderSortKind = kind;

    /// <summary>Raised after each subscribed <see cref="FsMapDiff"/> produces a non-empty <see cref="FlatModelDelta"/>.</summary>
    public event Action<FlatModelDelta>? DeltaReceived;

    public void Dispose()
    {
        if (_ownsSubscription)
            _diffStream.DiffReceived -= OnDiffReceived;
    }

    private void OnDiffReceived(FsMapDiff diff)
    {
        var delta = ApplyDiff(diff);
        if (!delta.IsEmpty)
            DeltaReceived?.Invoke(delta);
    }

    /// <summary>Rebuilds the entire flat projection from <see cref="FsMapWorkspace"/> + <see cref="ExpansionState"/>.</summary>
    /// <remarks>The index root row is not shown; depth 0 rows are immediate children of the root (implicitly expanded).</remarks>
    public FlatModelDelta Rebuild()
    {
        _rows.Clear();
        _rowIndexByPath.Clear();
        var root = _workspace.IndexRoot;
        if (_workspace.TryGetEntry(root, out var rootEntry) && rootEntry.HasSubfolders)
            AppendVisibleChildRows(root, childDepth: 0, _rows);
        RebuildPathIndex();
        return new FlatModelDelta(new[] { new FlatModelReset(_rows.ToArray()) });
    }

    /// <summary>Expands a folder (updates <see cref="ExpansionState"/>) and splices its visible descendant rows.</summary>
    public FlatModelDelta Expand(string folderPath)
    {
        var changes = new List<FlatModelChange>();
        var path = FavoriteIndexRoots.NormalizeFavoritePath(folderPath);
        // Index root has no row; its children are always visible when present.
        if (IsIndexRootExpandedForProjection(path))
            return FlatModelDelta.Empty;

        if (!Expansion.TryExpand(path))
            return FlatModelDelta.Empty;

        var ix = FindRowIndex(path);
        if (ix < 0)
            return FlatModelDelta.Empty;

        if (!_workspace.TryGetEntry(path, out var entry))
        {
            Expansion.TryCollapse(path);
            return FlatModelDelta.Empty;
        }

        var expandedRow = MakeRow(path, _rows[ix].Depth, entry, isExpanded: true);
        changes.Add(new FlatModelReplaceRow(ix, expandedRow));

        if (!entry.HasSubfolders)
            return Finalize(changes);

        // Under implicit root expansion, Rebuild already spliced immediate children; do not insert duplicates.
        if (IsIndexRootExpandedForProjection(path) && CountDescendantRowCount(ix, _rows[ix].Depth) > 0)
            return Finalize(changes);

        var inserted = new List<FolderRow>();
        AppendVisibleChildRows(path, _rows[ix].Depth + 1, inserted);
        if (inserted.Count > 0)
            changes.Add(new FlatModelInsertRange(ix + 1, inserted));

        ApplyChangesToRows(changes);
        RebuildPathIndex();
        return new FlatModelDelta(changes);
    }

    /// <summary>Collapses a folder and removes its visible descendant rows from the projection.</summary>
    public FlatModelDelta Collapse(string folderPath)
    {
        var changes = new List<FlatModelChange>();
        var path = FavoriteIndexRoots.NormalizeFavoritePath(folderPath);
        if (IsIndexRootExpandedForProjection(path))
            return FlatModelDelta.Empty;

        if (!Expansion.TryCollapse(path))
            return FlatModelDelta.Empty;

        var ix = FindRowIndex(path);
        if (ix < 0)
            return FlatModelDelta.Empty;

        if (!_workspace.TryGetEntry(path, out var entry))
            return FlatModelDelta.Empty;

        var d = _rows[ix].Depth;
        var removeCount = CountDescendantRowCount(ix, d);
        if (removeCount > 0)
            changes.Add(new FlatModelRemoveRange(ix + 1, removeCount));

        var collapsedRow = MakeRow(path, d, entry, isExpanded: false);
        changes.Add(new FlatModelReplaceRow(ix, collapsedRow));

        ApplyChangesToRows(changes);
        RebuildPathIndex();
        return new FlatModelDelta(changes);
    }

    /// <summary>Applies one map diff to the projection (no-op when <see cref="FsMapDiff.IndexRoot"/> does not match).</summary>
    public FlatModelDelta ApplyDiff(FsMapDiff diff)
    {
        if (!string.Equals(diff.IndexRoot, _workspace.IndexRoot, StringComparison.OrdinalIgnoreCase))
            return FlatModelDelta.Empty;

        return diff switch
        {
            FsFolderAddedDiff a => ApplyFolderAdded(a),
            FsFolderRemovedDiff r => ApplyFolderRemoved(r),
            FsFolderRenamedDiff m => ApplyFolderRenamed(m),
            FsFolderRefreshedDiff f => ApplyFolderRefreshed(f),
            FsAggregatesUpdatedDiff g => ApplyAggregatesUpdated(g),
            _ => FlatModelDelta.Empty,
        };
    }

    private FlatModelDelta ApplyFolderAdded(FsFolderAddedDiff a)
    {
        var changes = new List<FlatModelChange>();
        var path = FavoriteIndexRoots.NormalizeFavoritePath(a.Path);
        var parent = FavoriteIndexRoots.NormalizeFavoritePath(a.ParentPath);

        if (!AreAncestorsExpanded(path))
        {
            if (FindRowIndex(parent) >= 0 && _workspace.TryGetEntry(parent, out var pe))
                MaybeReplaceRowFromWorkspace(changes, parent, isExpanded: IsRowExpandedInProjection(parent));
            return Finalize(changes);
        }

        var root = _workspace.IndexRoot;
        int insertAt;
        int parentDepth;
        var parentIx = FindRowIndex(parent);
        if (parentIx >= 0)
        {
            parentDepth = _rows[parentIx].Depth;
            insertAt = FindSiblingInsertIndex(parentIx, path, a.Snapshot.Name);
        }
        else if (string.Equals(FavoriteIndexRoots.NormalizeFavoritePath(parent), root, StringComparison.OrdinalIgnoreCase))
        {
            parentDepth = -1;
            insertAt = FindSiblingInsertIndexUnderRoot(path, a.Snapshot.Name);
        }
        else
            return Finalize(changes);
        var slice = new List<FolderRow>();
        if (_workspace.TryGetEntry(path, out var e))
        {
            var childDepth = parentDepth + 1;
            slice.Add(MakeRow(path, childDepth, e, IsRowExpandedInProjection(path)));
            if (IsRowExpandedInProjection(path) && e.HasSubfolders)
                AppendVisibleChildRows(path, childDepth + 1, slice);
        }

        if (slice.Count > 0)
            changes.Add(new FlatModelInsertRange(insertAt, slice));

        if (_workspace.TryGetEntry(parent, out var parentEntry))
            MaybeReplaceRowFromWorkspace(changes, parent, IsRowExpandedInProjection(parent));

        return Finalize(changes);
    }

    private FlatModelDelta ApplyFolderRemoved(FsFolderRemovedDiff r)
    {
        var changes = new List<FlatModelChange>();
        var path = FavoriteIndexRoots.NormalizeFavoritePath(r.Path);
        var parent = FavoriteIndexRoots.NormalizeFavoritePath(r.ParentPath);

        Expansion.RemoveSubtreeKeys(path);
        AdjustSelectionAfterRemove(path);

        var ix = FindRowIndex(path);
        if (ix >= 0)
        {
            var d = _rows[ix].Depth;
            var count = 1 + CountDescendantRowCount(ix, d);
            changes.Add(new FlatModelRemoveRange(ix, count));
        }

        if (FindRowIndex(parent) >= 0 && _workspace.TryGetEntry(parent, out var pe))
            MaybeReplaceRowFromWorkspace(changes, parent, IsRowExpandedInProjection(parent));

        return Finalize(changes);
    }

    private FlatModelDelta ApplyFolderRenamed(FsFolderRenamedDiff m)
    {
        var changes = new List<FlatModelChange>();
        var oldPath = FavoriteIndexRoots.NormalizeFavoritePath(m.OldPath);
        var newPath = FavoriteIndexRoots.NormalizeFavoritePath(m.NewPath);

        Expansion.RemapSubtreePrefix(oldPath, newPath);
        RemapSelectionPrefix(oldPath, newPath);

        var oldIx = FindRowIndex(oldPath);
        if (oldIx >= 0)
        {
            var d = _rows[oldIx].Depth;
            var count = 1 + CountDescendantRowCount(oldIx, d);
            changes.Add(new FlatModelRemoveRange(oldIx, count));
        }

        if (AreAncestorsExpanded(newPath) && _workspace.TryGetEntry(newPath, out var ne))
        {
            var parentPath = FavoriteIndexRoots.NormalizeFavoritePath(m.NewParentPath);
            var root = _workspace.IndexRoot;
            int insertAt;
            int parentDepth;
            var parentIx = FindRowIndex(parentPath);
            if (parentIx >= 0)
            {
                parentDepth = _rows[parentIx].Depth;
                insertAt = FindSiblingInsertIndex(parentIx, newPath, ne.Name);
            }
            else if (string.Equals(parentPath, root, StringComparison.OrdinalIgnoreCase))
            {
                parentDepth = -1;
                insertAt = FindSiblingInsertIndexUnderRoot(newPath, ne.Name);
            }
            else
            {
                insertAt = -1;
                parentDepth = -1;
            }

            if (insertAt >= 0)
            {
                if (oldIx >= 0 && insertAt > oldIx)
                {
                    var removed = 1 + CountDescendantRowCount(_rows, oldIx, _rows[oldIx].Depth);
                    insertAt -= removed;
                }

                var slice = new List<FolderRow>
                {
                    MakeRow(newPath, parentDepth + 1, ne, IsRowExpandedInProjection(newPath)),
                };
                if (IsRowExpandedInProjection(newPath) && ne.HasSubfolders)
                    AppendVisibleChildRows(newPath, parentDepth + 2, slice);

                changes.Add(new FlatModelInsertRange(insertAt, slice));
            }
        }

        return Finalize(changes);
    }

    private FlatModelDelta ApplyFolderRefreshed(FsFolderRefreshedDiff f)
    {
        var changes = new List<FlatModelChange>();
        var path = FavoriteIndexRoots.NormalizeFavoritePath(f.Path);
        if (!_workspace.TryGetEntry(path, out var after))
            return FlatModelDelta.Empty;

        if (!after.HasSubfolders && Expansion.Contains(path) && !IsIndexRootExpandedForProjection(path))
        {
            Expansion.TryCollapse(path);
            var ix = FindRowIndex(path);
            if (ix >= 0)
            {
                var d = _rows[ix].Depth;
                var removeCount = CountDescendantRowCount(ix, d);
                if (removeCount > 0)
                    changes.Add(new FlatModelRemoveRange(ix + 1, removeCount));
            }
        }

        if (FindRowIndex(path) >= 0)
            MaybeReplaceRowFromWorkspace(changes, path, IsRowExpandedInProjection(path));

        return Finalize(changes);
    }

    private FlatModelDelta ApplyAggregatesUpdated(FsAggregatesUpdatedDiff g)
    {
        var path = FavoriteIndexRoots.NormalizeFavoritePath(g.Path);
        var ix = FindRowIndex(path);
        if (ix < 0 || !_workspace.TryGetEntry(path, out var mapEntry))
            return FlatModelDelta.Empty;

        var cur = _rows[ix];
        var merged = CloneEntryWithAggregates(mapEntry, g);
        var row = MakeRow(path, cur.Depth, merged, cur.IsExpanded);

        var changes = new List<FlatModelChange>();
        if (_folderSortKind is FolderListSortKind.AggregateSize or FolderListSortKind.ImageFileCount)
        {
            var temp = new List<FolderRow>(_rows);
            temp[ix] = row;
            if (TryAppendSiblingResortChanges(temp, ix, row.Depth, changes))
                return Finalize(changes);
        }

        changes.Add(new FlatModelReplaceRow(ix, row));
        return Finalize(changes);
    }

    private static FsMapEntry CloneEntryWithAggregates(FsMapEntry source, FsAggregatesUpdatedDiff g) =>
        new()
        {
            ParentPath = source.ParentPath,
            Name = source.Name,
            DirectoryMtimeUtc = source.DirectoryMtimeUtc,
            HasSubfolders = source.HasSubfolders,
            AggregateSizeBytes = g.AggregateSizeBytes,
            TotalFileCount = g.TotalFileCount,
            ImageFileCount = g.ImageFileCount,
            LastVerifiedAtUtc = source.LastVerifiedAtUtc,
        };

    private bool TryAppendSiblingResortChanges(
        List<FolderRow> temp,
        int updatedIx,
        int depth,
        List<FlatModelChange> sink)
    {
        var (start, end) = GetSiblingSubtreeSpan(temp, updatedIx, depth);
        var oldLen = end - start;
        if (oldLen <= 0)
            return false;

        var newSlice = BuildSortedSiblingSlice(temp, start, end, depth);
        if (newSlice.Count != oldLen)
            return false;

        var same = true;
        for (var i = 0; i < oldLen; i++)
        {
            if (!string.Equals(temp[start + i].Path, newSlice[i].Path, StringComparison.OrdinalIgnoreCase))
            {
                same = false;
                break;
            }
        }

        if (same)
            return false;

        sink.Add(new FlatModelRemoveRange(start, oldLen));
        sink.Add(new FlatModelInsertRange(start, newSlice));
        return true;
    }

    private List<FolderRow> BuildSortedSiblingSlice(IReadOnlyList<FolderRow> rows, int start, int end, int depth)
    {
        var heads = new List<int>();
        for (var i = start; i < end;)
        {
            if (rows[i].Depth == depth)
            {
                heads.Add(i);
                i += 1 + CountDescendantRowCount(rows, i, depth);
            }
            else
            {
                i++;
            }
        }

        heads.Sort((a, b) => CompareChildPaths(rows[a].Path, rows[b].Path));

        var result = new List<FolderRow>(end - start);
        foreach (var h in heads)
        {
            var c = 1 + CountDescendantRowCount(rows, h, depth);
            for (var k = 0; k < c; k++)
                result.Add(rows[h + k]);
        }

        return result;
    }

    private static (int start, int end) GetSiblingSubtreeSpan(
        IReadOnlyList<FolderRow> rows,
        int rowIx,
        int depth)
    {
        if (depth == 0)
        {
            var end = rows.Count;
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].Depth != 0)
                {
                    end = i;
                    break;
                }
            }

            return (0, end);
        }

        var parentDepth = depth - 1;
        var parentIx = -1;
        for (var i = rowIx - 1; i >= 0; i--)
        {
            if (rows[i].Depth == parentDepth)
            {
                parentIx = i;
                break;
            }
        }

        if (parentIx < 0)
            return (rowIx, Math.Min(rows.Count, rowIx + 1));

        var start = parentIx + 1;
        var end2 = rows.Count;
        for (var i = start; i < rows.Count; i++)
        {
            if (rows[i].Depth <= parentDepth)
            {
                end2 = i;
                break;
            }
        }

        return (start, end2);
    }

    private FlatModelDelta Finalize(List<FlatModelChange> changes)
    {
        if (changes.Count == 0)
            return FlatModelDelta.Empty;
        ApplyChangesToRows(changes);
        RebuildPathIndex();
        return new FlatModelDelta(changes);
    }

    private void MaybeReplaceRowFromWorkspace(List<FlatModelChange> changes, string path, bool isExpanded)
    {
        var ix = FindRowIndex(path);
        if (ix < 0 || !_workspace.TryGetEntry(path, out var e))
            return;
        changes.Add(new FlatModelReplaceRow(ix, MakeRow(path, _rows[ix].Depth, e, isExpanded)));
    }

    /// <summary>Index root is always treated as expanded for projection so immediate children are visible without persisting root in <see cref="ExpansionState"/>.</summary>
    private bool IsIndexRootExpandedForProjection(string path)
    {
        var root = _workspace.IndexRoot;
        return string.Equals(FavoriteIndexRoots.NormalizeFavoritePath(path), root, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsRowExpandedInProjection(string path) =>
        Expansion.Contains(path) || IsIndexRootExpandedForProjection(path);

    private void AppendVisibleChildRows(string parentPath, int childDepth, List<FolderRow> sink)
    {
        foreach (var child in GetSortedChildPaths(parentPath))
        {
            if (!_workspace.TryGetEntry(child, out var e))
                continue;
            sink.Add(MakeRow(child, childDepth, e, IsRowExpandedInProjection(child)));
            if (IsRowExpandedInProjection(child) && e.HasSubfolders)
                AppendVisibleChildRows(child, childDepth + 1, sink);
        }
    }

    private List<string> GetSortedChildPaths(string parentPath)
    {
        var raw = _workspace.GetDirectChildPaths(parentPath);
        var list = raw.ToList();
        list.Sort(CompareChildPaths);
        return list;
    }

    private int CompareChildPaths(string pathA, string pathB)
    {
        _workspace.TryGetEntry(pathA, out var ea);
        _workspace.TryGetEntry(pathB, out var eb);
        var cmp = _folderSortKind switch
        {
            FolderListSortKind.DateModified => CompareEntryDateModified(ea, eb),
            FolderListSortKind.AggregateSize => CompareEntryAggregateSize(ea, eb),
            FolderListSortKind.ImageFileCount => CompareEntryImageCount(ea, eb),
            _ => 0,
        };
        if (cmp != 0)
            return cmp;
        return CompareEntryNameNatural(pathA, pathB, ea, eb);
    }

    private static int CompareEntryDateModified(FsMapEntry? a, FsMapEntry? b)
    {
        var ta = a?.DirectoryMtimeUtc;
        var tb = b?.DirectoryMtimeUtc;
        if (ta == null && tb == null)
            return 0;
        if (ta == null)
            return 1;
        if (tb == null)
            return -1;
        return tb.Value.CompareTo(ta.Value);
    }

    private static int CompareEntryAggregateSize(FsMapEntry? a, FsMapEntry? b)
    {
        var sa = a?.AggregateSizeBytes ?? 0;
        var sb = b?.AggregateSizeBytes ?? 0;
        return sb.CompareTo(sa);
    }

    private static int CompareEntryImageCount(FsMapEntry? a, FsMapEntry? b)
    {
        var ia = a?.ImageFileCount ?? 0;
        var ib = b?.ImageFileCount ?? 0;
        return ib.CompareTo(ia);
    }

    private int CompareEntryNameNatural(string pathA, string pathB, FsMapEntry? ea, FsMapEntry? eb)
    {
        var nameA = ea is { Name: { Length: > 0 } n1 } ? n1 : Path.GetFileName(pathA);
        var nameB = eb is { Name: { Length: > 0 } n2 } ? n2 : Path.GetFileName(pathB);
        var c = NaturalStringComparer.OrdinalIgnoreCase.Compare(nameA, nameB);
        if (c != 0)
            return c;
        return string.Compare(pathA, pathB, StringComparison.OrdinalIgnoreCase);
    }

    private FolderRow MakeRow(string path, int depth, FsMapEntry entry, bool isExpanded) =>
        new()
        {
            IndexRoot = _workspace.IndexRoot,
            Path = path,
            Depth = depth,
            IsExpanded = isExpanded,
            HasChildren = entry.HasSubfolders,
            Name = string.IsNullOrEmpty(entry.Name) ? Path.GetFileName(path) : entry.Name,
            AggregateSizeBytes = entry.AggregateSizeBytes,
            TotalFileCount = entry.TotalFileCount,
            ImageFileCount = entry.ImageFileCount,
            SizeDisplay = FolderRowFormatting.FormatSize(entry.AggregateSizeBytes),
            ImageCountDisplay = FolderRowFormatting.FormatImageCount(entry.ImageFileCount),
            ModifiedDisplay = FolderRowFormatting.FormatModified(entry.DirectoryMtimeUtc),
        };

    private int FindRowIndex(string path)
    {
        var p = FavoriteIndexRoots.NormalizeFavoritePath(path);
        return _rowIndexByPath.TryGetValue(p, out var ix) ? ix : -1;
    }

    private void RebuildPathIndex()
    {
        _rowIndexByPath.Clear();
        for (var i = 0; i < _rows.Count; i++)
            _rowIndexByPath[_rows[i].Path] = i;
    }

    private static int CountDescendantRowCount(IReadOnlyList<FolderRow> rows, int rowIndex, int rowDepth)
    {
        var c = 0;
        for (var j = rowIndex + 1; j < rows.Count; j++)
        {
            if (rows[j].Depth <= rowDepth)
                break;
            c++;
        }

        return c;
    }

    private int CountDescendantRowCount(int rowIndex, int rowDepth) =>
        CountDescendantRowCount(_rows, rowIndex, rowDepth);

    private int FindSiblingInsertIndex(int parentRowIndex, string newChildPath, string newChildName)
    {
        var pd = _rows[parentRowIndex].Depth;
        var targetDepth = pd + 1;
        var i = parentRowIndex + 1;
        while (i < _rows.Count)
        {
            var rd = _rows[i].Depth;
            if (rd <= pd)
                break;
            if (rd == targetDepth)
            {
                var cmp = CompareChildPaths(newChildPath, _rows[i].Path);
                if (cmp < 0)
                    break;
                if (string.Equals(_rows[i].Path, newChildPath, StringComparison.OrdinalIgnoreCase))
                    break;
            }

            i++;
        }

        return i;
    }

    /// <summary>Insert position among depth-0 rows (children of index root, which has no row).</summary>
    private int FindSiblingInsertIndexUnderRoot(string newChildPath, string newChildName)
    {
        var i = 0;
        while (i < _rows.Count && _rows[i].Depth == 0)
        {
            var cmp = CompareChildPaths(newChildPath, _rows[i].Path);
            if (cmp < 0)
                break;
            if (string.Equals(_rows[i].Path, newChildPath, StringComparison.OrdinalIgnoreCase))
                break;
            i++;
        }

        return i;
    }

    private bool AreAncestorsExpanded(string path)
    {
        var root = _workspace.IndexRoot;
        var p = FavoriteIndexRoots.NormalizeFavoritePath(path);
        while (!string.Equals(p, root, StringComparison.OrdinalIgnoreCase))
        {
            var parent = FsMapPathHelpers.ParentPathOrEmpty(p, root);
            if (string.IsNullOrEmpty(parent))
                return false;
            if (!Expansion.Contains(parent) && !string.Equals(parent, root, StringComparison.OrdinalIgnoreCase))
                return false;
            p = parent;
        }

        return true;
    }

    private void AdjustSelectionAfterRemove(string removedPath)
    {
        var sel = Selection.SelectedFolderPath;
        if (string.IsNullOrEmpty(sel))
            return;
        if (string.Equals(sel, removedPath, StringComparison.OrdinalIgnoreCase))
        {
            Selection.SelectedFolderPath = FsMapPathHelpers.ParentPathOrEmpty(removedPath, _workspace.IndexRoot);
            return;
        }

        if (FavoriteIndexRoots.IsStrictSubpath(sel, removedPath))
            Selection.SelectedFolderPath = FsMapPathHelpers.ParentPathOrEmpty(removedPath, _workspace.IndexRoot);
    }

    private void RemapSelectionPrefix(string oldPrefix, string newPrefix)
    {
        var sel = Selection.SelectedFolderPath;
        if (string.IsNullOrEmpty(sel))
            return;
        if (string.Equals(sel, oldPrefix, StringComparison.OrdinalIgnoreCase))
        {
            Selection.SelectedFolderPath = newPrefix;
            return;
        }

        if (!FavoriteIndexRoots.IsStrictSubpath(sel, oldPrefix))
            return;
        var rel = RelativeChildPath(oldPrefix, sel);
        Selection.SelectedFolderPath = string.IsNullOrEmpty(rel)
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

    private void ApplyChangesToRows(List<FlatModelChange> changes)
    {
        foreach (var ch in changes)
        {
            switch (ch)
            {
                case FlatModelReset reset:
                    _rows.Clear();
                    _rows.AddRange(reset.Rows);
                    return;
                case FlatModelRemoveRange rr:
                    _rows.RemoveRange(rr.StartIndex, rr.Count);
                    break;
                case FlatModelInsertRange ir:
                    _rows.InsertRange(ir.StartIndex, ir.Rows);
                    break;
                case FlatModelReplaceRow rr:
                    _rows[rr.Index] = rr.Row;
                    break;
            }
        }
    }
}
