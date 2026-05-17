using ImageHoard.Core.Browse;
using ImageHoard.Core.Browse2;
using Microsoft.UI.Dispatching;
using System.Linq;

namespace ImageHoard.App.BrowserV2;

/// <summary>
/// UI-thread orchestrator for <see cref="FolderTreeFlatModel"/>: FsMap diffs are queued from any thread and applied in one batched <see cref="FlatModelDelta"/> per dispatcher callback (replaces generation-token interleaving with the map as single writer).
/// </summary>
internal sealed class TreeController : IDisposable
{
    private readonly FsMapWorkspace _workspace;
    private readonly FsDiffStream _diffStream;
    private readonly DispatcherQueue _dispatcher;
    private readonly FolderTreeFlatModel _model;
    private readonly object _pendingLock = new();
    private readonly List<FsMapDiff> _pendingDiffs = new();
    private bool _flushScheduled;

    public TreeController(FsMapWorkspace workspace, FsDiffStream diffStream, DispatcherQueue dispatcher)
    {
        _workspace = workspace;
        _diffStream = diffStream;
        _dispatcher = dispatcher;
        _model = new FolderTreeFlatModel(workspace, diffStream, subscribeToDiffStream: false);
        _diffStream.DiffReceived += OnDiffReceived;
    }

    public FsMapWorkspace Workspace => _workspace;

    public FolderTreeFlatModel Model => _model;

    /// <summary>Raised on the dispatcher after one coalesced application pass.</summary>
    public event Action<FlatModelDelta>? TreeModelDelta;

    /// <summary>
    /// Raised on the dispatcher after <see cref="RevealAndSelect"/> updates <see cref="FolderTreeFlatModel.Selection"/>,
    /// including when there is no structural delta (e.g. sibling folder under an already-expanded parent).
    /// </summary>
    public event Action? AfterRevealAndSelect;

    public void Dispose()
    {
        _diffStream.DiffReceived -= OnDiffReceived;
        _model.Dispose();
    }

    /// <summary>Load expansion from cold-boot snapshot, rebuild visible rows from the hydrated map, assign selection (persisted path clamped to map, then <paramref name="initialSelectedFolder"/>).</summary>
    public FlatModelDelta ColdBootFromStore(
        BrowserTreeStore? store,
        string? initialSelectedFolder,
        FolderListSortKind initialFolderListSort,
        string normalizedTreeDisplayRoot)
    {
        _model.InitializeFolderSortKind(initialFolderListSort);
        _model.SetTreeDisplayRoot(normalizedTreeDisplayRoot);
        _model.Expansion.Clear();
        if (store?.ExpandedFolderPaths is { Count: > 0 } paths)
            _model.Expansion.Load(paths);

        FilterExpansionToDisplaySubtree();

        var delta = _model.Rebuild();
        var sel = FsColdBootPathResolver.ResolveSelectedFolderForColdBoot(
            _workspace,
            _workspace.IndexRoot,
            _model.TreeDisplayRoot,
            store?.SelectedFolderPath,
            initialSelectedFolder);
        var normDisplayRoot = FavoriteIndexRoots.NormalizeFavoritePath(_model.TreeDisplayRoot);
        if (!string.IsNullOrWhiteSpace(sel)
            && _model.Rows.Count > 0
            && string.Equals(FavoriteIndexRoots.NormalizeFavoritePath(sel), normDisplayRoot, StringComparison.OrdinalIgnoreCase))
            sel = _model.Rows[0].Path;
        _model.Selection.SelectedFolderPath = sel;
        RaiseIfNonEmpty(delta);
        return delta;
    }

    /// <summary>When the browse folder changes under the same <see cref="FsMapWorkspace.IndexRoot"/>, updates the visible subtree root and rebuilds rows.</summary>
    public FlatModelDelta SyncTreeDisplayRoot(string normalizedBrowseFolder)
    {
        EnsureDispatcherThread();
        var clamped = Browse2TreeDisplayRoot.ClampToWorkspace(_workspace, normalizedBrowseFolder);
        if (string.Equals(clamped, _model.TreeDisplayRoot, StringComparison.OrdinalIgnoreCase))
            return FlatModelDelta.Empty;

        _model.SetTreeDisplayRoot(clamped);
        FilterExpansionToDisplaySubtree();
        var delta = _model.Rebuild();
        _model.ClampSelectionToTreeDisplaySubtree();
        RaiseIfNonEmpty(delta);
        return delta;
    }

    private void FilterExpansionToDisplaySubtree()
    {
        var dr = _model.TreeDisplayRoot;
        foreach (var p in _model.Expansion.ExpandedPaths.ToList())
        {
            if (Browse2TreeDisplayRoot.IsSameOrStrictDescendantOf(dr, p))
                continue;
            _model.Expansion.TryCollapse(p);
        }
    }

    public FlatModelDelta ExpandFolder(string folderPath)
    {
        EnsureDispatcherThread();
        var delta = _model.Expand(folderPath);
        RaiseIfNonEmpty(delta);
        return delta;
    }

    public FlatModelDelta CollapseFolder(string folderPath)
    {
        EnsureDispatcherThread();
        var delta = _model.Collapse(folderPath);
        RaiseIfNonEmpty(delta);
        return delta;
    }

    /// <summary>Expand/collapse toggle for a row that has children.</summary>
    public FlatModelDelta ToggleExpand(string folderPath)
    {
        EnsureDispatcherThread();
        var p = FavoriteIndexRoots.NormalizeFavoritePath(folderPath);
        if (!_workspace.TryGetEntry(p, out var e) || !e.HasSubfolders)
            return FlatModelDelta.Empty;

        var delta = _model.Expansion.Contains(p) ? _model.Collapse(p) : _model.Expand(p);
        RaiseIfNonEmpty(delta);
        return delta;
    }

    public FlatModelDelta SetFolderSortKind(FolderListSortKind kind)
    {
        EnsureDispatcherThread();
        var delta = _model.SetFolderSortKind(kind);
        RaiseIfNonEmpty(delta);
        return delta;
    }

    public void SetSelectedFolder(string? folderPath)
    {
        EnsureDispatcherThread();
        _model.Selection.SelectedFolderPath = string.IsNullOrWhiteSpace(folderPath)
            ? null
            : FavoriteIndexRoots.NormalizeFavoritePath(folderPath);
    }

    /// <summary>Ensures ancestor chain is expanded so <paramref name="folderPath"/> exists in <see cref="FolderTreeFlatModel.Rows"/>, then selects it.</summary>
    public FlatModelDelta RevealAndSelect(string folderPath)
    {
        EnsureDispatcherThread();
        var norm = FavoriteIndexRoots.NormalizeFavoritePath(folderPath);
        var displayRoot = _model.TreeDisplayRoot;
        var merged = new List<FlatModelChange>();
        var chain = new List<string>();
        for (var c = ParentPathOrEmpty(norm, displayRoot);
             !string.IsNullOrEmpty(c);
             c = ParentPathOrEmpty(c, displayRoot))
        {
            chain.Add(c);
        }

        chain.Reverse();
        foreach (var anc in chain)
        {
            var d = _model.Expand(anc);
            AppendChanges(merged, d);
        }

        _model.Selection.SelectedFolderPath = norm;
        var delta = merged.Count == 0 ? FlatModelDelta.Empty : new FlatModelDelta(merged);
        RaiseIfNonEmpty(delta);
        AfterRevealAndSelect?.Invoke();
        return delta;
    }

    private void OnDiffReceived(FsMapDiff diff)
    {
        if (!string.Equals(diff.IndexRoot, _workspace.IndexRoot, StringComparison.OrdinalIgnoreCase))
            return;

        lock (_pendingLock)
        {
            _pendingDiffs.Add(diff);
            if (_flushScheduled)
                return;
            _flushScheduled = true;
        }

        _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, FlushPendingDiffsOnDispatcher);
    }

    private void FlushPendingDiffsOnDispatcher()
    {
        List<FsMapDiff> batch;
        lock (_pendingLock)
        {
            batch = new List<FsMapDiff>(_pendingDiffs);
            _pendingDiffs.Clear();
            _flushScheduled = false;
        }

        if (batch.Count == 0)
            return;

        var merged = new List<FlatModelChange>();
        foreach (var d in batch)
        {
            var delta = _model.ApplyDiff(d);
            AppendChanges(merged, delta);
        }

        if (merged.Count > 0)
            RaiseIfNonEmpty(new FlatModelDelta(merged));
    }

    private void RaiseIfNonEmpty(FlatModelDelta delta)
    {
        if (!delta.IsEmpty)
            TreeModelDelta?.Invoke(delta);
    }

    private static void AppendChanges(List<FlatModelChange> sink, FlatModelDelta delta)
    {
        if (delta.IsEmpty)
            return;
        sink.AddRange(delta.Changes);
    }

    private void EnsureDispatcherThread()
    {
        if (!_dispatcher.HasThreadAccess)
            throw new InvalidOperationException("TreeController folder mutations must run on the owning DispatcherQueue thread.");
    }

    private static string ParentPathOrEmpty(string fullPath, string indexRoot)
    {
        var n = FavoriteIndexRoots.NormalizeFavoritePath(fullPath);
        var r = FavoriteIndexRoots.NormalizeFavoritePath(indexRoot);
        if (string.Equals(n, r, StringComparison.OrdinalIgnoreCase))
            return "";
        var p = Path.GetDirectoryName(n);
        return string.IsNullOrEmpty(p) ? "" : FavoriteIndexRoots.NormalizeFavoritePath(p);
    }
}
