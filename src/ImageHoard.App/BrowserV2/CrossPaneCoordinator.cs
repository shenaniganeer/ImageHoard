using ImageHoard.Core.Browse;
using ImageHoard.Core.Browse2;
using ImageHoard.Core.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using System.Threading;

namespace ImageHoard.App.BrowserV2;

/// <summary>
/// Glue for Browse2: one <see cref="FsMapWorkspace"/> writer with <see cref="FsDiffStream"/> fan-out, dispatcher-serialized tree projection updates, sibling image pane, find, and wizard-oriented mutation gate (replaces <c>_populateBrowserGeneration</c> / nested mutation depth for this pane).
/// </summary>
internal sealed class CrossPaneCoordinator : IDisposable
{
    private FolderTreeView? _folderTreeView;
    private string _treeDisplayRoot = "";
    private EventHandler<object>? _firstPaintScannerRenderingHandler;
    private FsBackgroundScanner? _firstPaintPendingScanner;
    private CancellationToken _firstPaintScannerCancellation;
    private CancellationToken _listingRefreshCancellation;
    private bool _pendingColdBootViewportRestore;

    public CrossPaneCoordinator(
        IFileSystem fileSystem,
        FsMapRegistry registry,
        FsMapWorkspace workspace,
        DispatcherQueue dispatcher)
    {
        FileSystem = fileSystem;
        Registry = registry;
        Workspace = workspace;
        Dispatcher = dispatcher;
        TargetedRefresher = new FsTargetedRefresher(fileSystem, registry);
        ChangeApplier = new FsChangeApplier(TargetedRefresher);
        Tree = new TreeController(workspace, registry.DiffStream, dispatcher);
        Images = new ImagePaneController(fileSystem, registry.DiffStream, dispatcher);
        Find = new BrowserFindController(this, registry);
        Mutations = new BrowserPaneMutationGate();

        Tree.TreeModelDelta += OnTreeModelDelta;
        Tree.AfterRevealAndSelect += OnTreeAfterRevealAndSelect;
        Images.SelectedImagePathChanged += OnImagesSelectedImagePathChanged;
        Registry.DiffStream.DiffReceived += OnRegistryDiffForParentListing;
    }

    public IFileSystem FileSystem { get; }

    public FsMapRegistry Registry { get; }

    public FsMapWorkspace Workspace { get; }

    public DispatcherQueue Dispatcher { get; }

    public FsTargetedRefresher TargetedRefresher { get; }

    public FsChangeApplier ChangeApplier { get; }

    public TreeController Tree { get; }

    public ImagePaneController Images { get; }

    public BrowserFindController Find { get; }

    public BrowserPaneMutationGate Mutations { get; }

    /// <summary>When non-null, <see cref="CaptureBrowserTreeIntoStore"/> writes expansion/selection for persistence.</summary>
    public BrowserTreeStore? TreeStore { get; private set; }

    public event EventHandler<string?>? SelectedImagePathChanged;

    public event EventHandler<string?>? SelectedFolderPathChanged;

    /// <summary>Attach UI; call <see cref="ColdBoot"/> before or after — if after, this pushes the current model rows into the view.</summary>
    public void AttachFolderTreeView(FolderTreeView tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        DetachFolderTreeView();
        _folderTreeView = tree;
        tree.IndexRoot = string.IsNullOrEmpty(_treeDisplayRoot) ? Workspace.IndexRoot : _treeDisplayRoot;
        tree.ToggleExpandRequested += OnFolderTreeToggleExpand;
        tree.SelectedFolderPathChanged += OnFolderTreeSelectedPathChanged;
        tree.ResetRows(Tree.Model.Rows, preserveViewport: false);
        tree.SelectedFolderPath = Tree.Model.Selection.SelectedFolderPath;
        if (_pendingColdBootViewportRestore && TreeStore?.ViewportAnchor is { AnchorFolderPath: { Length: > 0 } ap } va)
        {
            tree.RestorePersistedViewportAnchor(ap, va.OffsetWithinRowPx);
            _pendingColdBootViewportRestore = false;
        }
    }

    public void DetachFolderTreeView()
    {
        if (_folderTreeView is not { } t)
            return;
        t.ToggleExpandRequested -= OnFolderTreeToggleExpand;
        t.SelectedFolderPathChanged -= OnFolderTreeSelectedPathChanged;
        _folderTreeView = null;
    }

    /// <summary>Rebuild tree projection from persisted expansion + selection; restore viewport anchor; optionally defer background map scan until after first frame.</summary>
    public void ColdBoot(
        BrowserTreeStore? store,
        string? initialSelectedFolder,
        FolderListSortKind initialFolderListSort,
        string normalizedTreeDisplayRoot,
        FsBackgroundScanner? backgroundScanner = null,
        CancellationToken appCancellationToken = default)
    {
        _treeDisplayRoot = Browse2TreeDisplayRoot.ClampToWorkspace(Workspace, normalizedTreeDisplayRoot);
        _ = Tree.ColdBootFromStore(store, initialSelectedFolder, initialFolderListSort, _treeDisplayRoot);
        TreeStore = store;
        Images.OwningIndexRoot = Workspace.IndexRoot;
        Images.CurrentFolderPath = Tree.Model.Selection.SelectedFolderPath;
        if (_folderTreeView is not null)
        {
            _folderTreeView.ResetRows(Tree.Model.Rows, preserveViewport: false);
            _folderTreeView.SelectedFolderPath = Tree.Model.Selection.SelectedFolderPath;
            _folderTreeView.RestorePersistedViewportAnchor(
                store?.ViewportAnchor?.AnchorFolderPath,
                store?.ViewportAnchor?.OffsetWithinRowPx ?? 0);
            _pendingColdBootViewportRestore = false;
        }
        else
            _pendingColdBootViewportRestore = store?.ViewportAnchor is { AnchorFolderPath: { Length: > 0 } };

        if (backgroundScanner is not null)
            ScheduleBackgroundScannerAfterFirstPaint(backgroundScanner, appCancellationToken);

        _listingRefreshCancellation = appCancellationToken;
        _ = TargetedRefresher.RefreshAsync(Workspace.IndexRoot, _listingRefreshCancellation);
    }

    /// <summary>Updates the visible subtree when the browse folder moves under the same persisted map root.</summary>
    public FlatModelDelta SyncBrowseTreeDisplayRoot(string browseFolder)
    {
        var clamped = Browse2TreeDisplayRoot.ClampToWorkspace(Workspace, browseFolder);
        if (string.Equals(clamped, _treeDisplayRoot, StringComparison.OrdinalIgnoreCase))
            return FlatModelDelta.Empty;

        _treeDisplayRoot = clamped;
        var delta = Tree.SyncTreeDisplayRoot(browseFolder);
        if (_folderTreeView is not null)
        {
            _folderTreeView.IndexRoot = _treeDisplayRoot;
            if (!delta.IsEmpty)
            {
                _folderTreeView.ApplyModelDelta(delta, preserveViewport: true);
                _folderTreeView.SelectedFolderPath = Tree.Model.Selection.SelectedFolderPath;
            }
        }

        return delta;
    }

    /// <summary>DFS aggregate refresh for each immediate child folder under <paramref name="parentFolderPath"/> (Browse2 size/image-count sorts).</summary>
    public Task EnsureAggregatesForVisibleChildrenAsync(string parentFolderPath, CancellationToken cancellationToken = default) =>
        TargetedRefresher.RefreshAggregatesForDirectChildrenAsync(parentFolderPath, cancellationToken);

    /// <summary>Re-lists one folder on disk and merges immediate child directory rows into the FsMap (raises diffs).</summary>
    public Task RefreshFolderListingAsync(string folderPath, CancellationToken cancellationToken = default) =>
        TargetedRefresher.RefreshAsync(folderPath, cancellationToken);
    public void CaptureBrowserTreeIntoStore()
    {
        if (TreeStore == null)
            return;
        TreeStore.ExpandedFolderPaths.Clear();
        TreeStore.ExpandedFolderPaths.AddRange(Tree.Model.Expansion.ExpandedPaths);
        TreeStore.SelectedFolderPath = Tree.Model.Selection.SelectedFolderPath;
        if (_folderTreeView?.GetPersistedViewportAnchor() is { } anchor)
            TreeStore.ViewportAnchor = anchor;
    }

    /// <summary>Loads on-disk FsMap documents for every workspace in the registry; call before <see cref="ColdBoot"/>.</summary>
    public Task LoadFsMapsFromDiskAsync(CancellationToken cancellationToken = default) =>
        Registry.LoadAllAsync(cancellationToken);

    /// <summary>Starts <see cref="FsBackgroundScanner"/> once after the first UI frame (per instance).</summary>
    public void ScheduleBackgroundScannerAfterFirstPaint(FsBackgroundScanner scanner, CancellationToken appCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        if (scanner.HasStarted)
            return;
        UnhookFirstPaintScanner();
        _firstPaintPendingScanner = scanner;
        _firstPaintScannerCancellation = appCancellationToken;
        _firstPaintScannerRenderingHandler = OnCompositionTargetRenderingForScannerStart;
        CompositionTarget.Rendering += _firstPaintScannerRenderingHandler;
    }

    public bool TryGetSlideshowOverlayStyleBrowseFlatLinePosition(
        string? imagePath,
        out int index1Based,
        out int total,
        out int discoveredApprox)
    {
        index1Based = 0;
        total = 0;
        discoveredApprox = 0;
        if (string.IsNullOrEmpty(imagePath))
            return false;

        var norm = FavoriteIndexRoots.NormalizeFavoritePath(imagePath);
        var list = Images.Items;
        total = list.Count;
        discoveredApprox = total;
        if (total == 0)
            return false;

        for (var i = 0; i < list.Count; i++)
        {
            if (!string.Equals(list[i].FullPath, norm, StringComparison.OrdinalIgnoreCase))
                continue;
            index1Based = i + 1;
            return true;
        }

        return false;
    }

    internal void ApplyFindFolderHit(string folderPath)
    {
        _ = Tree.RevealAndSelect(folderPath);
        Images.CurrentFolderPath = Tree.Model.Selection.SelectedFolderPath;
        _folderTreeView?.ScrollFolderIntoView(folderPath, centerInViewport: true);
        _ = TargetedRefresher.RefreshAsync(folderPath, _listingRefreshCancellation);
    }

    internal void ApplyFindFileHit(string imagePath)
    {
        Images.SelectByPath(imagePath);
        var parent = Path.GetDirectoryName(imagePath);
        if (!string.IsNullOrEmpty(parent))
        {
            var p = FavoriteIndexRoots.NormalizeFavoritePath(parent);
            _ = Tree.RevealAndSelect(p);
            Images.CurrentFolderPath = Tree.Model.Selection.SelectedFolderPath;
            _folderTreeView?.ScrollFolderIntoView(p, centerInViewport: false);
            _ = TargetedRefresher.RefreshAsync(p, _listingRefreshCancellation);
        }
    }

    public void Dispose()
    {
        Registry.DiffStream.DiffReceived -= OnRegistryDiffForParentListing;
        Tree.TreeModelDelta -= OnTreeModelDelta;
        Tree.AfterRevealAndSelect -= OnTreeAfterRevealAndSelect;
        Images.SelectedImagePathChanged -= OnImagesSelectedImagePathChanged;
        UnhookFirstPaintScanner();
        DetachFolderTreeView();
        Images.Dispose();
        Tree.Dispose();
    }

    private void OnRegistryDiffForParentListing(FsMapDiff diff)
    {
        if (!string.Equals(diff.IndexRoot, Workspace.IndexRoot, StringComparison.OrdinalIgnoreCase))
            return;
        if (diff is not FsFolderRemovedDiff removed)
            return;

        var parent = FavoriteIndexRoots.NormalizeFavoritePath(removed.ParentPath);
        if (string.IsNullOrEmpty(parent))
            return;

        _ = Dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (Workspace.GetDirectChildPaths(parent).Count == 0)
                _ = TargetedRefresher.RefreshAsync(parent, _listingRefreshCancellation);
        });
    }

    private void OnTreeModelDelta(FlatModelDelta delta)
    {
        _folderTreeView?.ApplyModelDelta(delta, preserveViewport: true);
        SyncFolderTreeViewSelectedPathFromModel();
    }

    private void OnTreeAfterRevealAndSelect() => SyncFolderTreeViewSelectedPathFromModel();

    private void SyncFolderTreeViewSelectedPathFromModel()
    {
        if (_folderTreeView is null)
            return;
        var p = Tree.Model.Selection.SelectedFolderPath;
        if (!string.Equals(_folderTreeView.SelectedFolderPath, p, StringComparison.OrdinalIgnoreCase))
            _folderTreeView.SelectedFolderPath = p;
    }

    private void OnFolderTreeToggleExpand(FolderTreeView sender, string path)
    {
        var p = FavoriteIndexRoots.NormalizeFavoritePath(path);
        var wasExpanded = Tree.Model.Expansion.Contains(p);
        _ = Tree.ToggleExpand(p);
        var nowExpanded = Tree.Model.Expansion.Contains(p);
        if (!wasExpanded && nowExpanded && ShouldRefreshListingAfterExpand(p))
            _ = TargetedRefresher.RefreshAsync(p, _listingRefreshCancellation);
    }

    private void OnFolderTreeSelectedPathChanged(FolderTreeView sender, string path)
    {
        Tree.SetSelectedFolder(path);
        if (!string.Equals(Images.CurrentFolderPath, path, StringComparison.OrdinalIgnoreCase))
            Images.CurrentFolderPath = path;
        SelectedFolderPathChanged?.Invoke(this, path);
    }

    private void OnImagesSelectedImagePathChanged(object? sender, string? path) =>
        SelectedImagePathChanged?.Invoke(this, path);

    private void OnCompositionTargetRenderingForScannerStart(object? sender, object e)
    {
        if (_firstPaintScannerRenderingHandler is null)
            return;
        CompositionTarget.Rendering -= _firstPaintScannerRenderingHandler;
        _firstPaintScannerRenderingHandler = null;
        var scanner = _firstPaintPendingScanner;
        _firstPaintPendingScanner = null;
        if (scanner is not null)
            scanner.StartOnce(Registry, FileSystem, _firstPaintScannerCancellation);
    }

    private void UnhookFirstPaintScanner()
    {
        if (_firstPaintScannerRenderingHandler is not null)
        {
            CompositionTarget.Rendering -= _firstPaintScannerRenderingHandler;
            _firstPaintScannerRenderingHandler = null;
        }

        _firstPaintPendingScanner = null;
    }

    private bool ShouldRefreshListingAfterExpand(string folderPath)
    {
        if (!Workspace.TryGetEntry(folderPath, out var e))
            return true;
        if (e.LastVerifiedAtUtc == null)
            return true;
        if (e.HasSubfolders && Workspace.GetDirectChildPaths(folderPath).Count == 0)
            return true;
        return false;
    }
}
