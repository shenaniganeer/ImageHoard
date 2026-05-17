using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageHoard.App.BrowserV2;
using ImageHoard.Core;
using ImageHoard.Core.Browse;
using ImageHoard.Core.Browse2;
using ImageHoard.Core.Metrics;
using ImageHoard.Core.Services;
using ImageHoard.Core.Sort;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using VirtualKey = Windows.System.VirtualKey;

namespace ImageHoard.App;

public sealed partial class MainWindow
{
    internal readonly record struct WizardPredeletedFileStat(string FullPath, long LengthBytes, bool IsImage);

    internal readonly record struct WizardRestoredFileStat(string FullPath, long LengthBytes, bool IsImage);

    private readonly Dictionary<string, long?> _folderAggregateBytesByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int?> _folderImageFileCountByPath = new(StringComparer.OrdinalIgnoreCase);

    private bool _suppressBrowserTreeViewportMutationForColdBoot;

    private TreeViewNode? _browserContextMenuTargetNode;
    private MenuFlyout? _browserTreeContextMenu;
    private bool _browserContextMenuIsToolbarCurrentFolder;
    private MenuFlyoutItem? _browserTreeContextMenuRenameItem;
    private TreeViewNode? _browse2ContextMenuTargetWrapNode;

    private readonly BrowserFolderListHeaderMarker _browse2FolderListHeaderMarker = new();
    private readonly BrowserFileListHeaderMarker _browse2FileListHeaderMarker = new();

    private readonly List<BrowserFindMatch> _browserFindMatches = new();
    private int _browserFindCurrentIndex;
    private CancellationTokenSource? _browserFindSearchCts;
    private BrowserFindSearchParameters? _browserFindMatchesForParameters;

    internal enum BrowserFindSearchAnchor
    {
        First,
        Last,
    }

    internal bool IsBrowserFindOverlayOpen =>
        BrowserFindOverlayRoot.Visibility == Visibility.Visible;

    private void WireBrowseShellChrome()
    {
        _browserTreeContextMenu = new MenuFlyout();
        var refreshItem = new MenuFlyoutItem { Text = "Refresh" };
        refreshItem.Click += (_, _) => BrowserContextRefresh_Click();
        _browserTreeContextMenu.Items.Add(refreshItem);
        _browserTreeContextMenuRenameItem = new MenuFlyoutItem { Text = "Rename" };
        _browserTreeContextMenuRenameItem.Click += (_, _) => BrowserContextRename_Click();
        _browserTreeContextMenu.Items.Add(_browserTreeContextMenuRenameItem);
        var deleteItem = new MenuFlyoutItem { Text = "Delete" };
        deleteItem.Click += BrowserContextDelete_Click;
        _browserTreeContextMenu.Items.Add(deleteItem);
        var revealItem = new MenuFlyoutItem { Text = "Reveal in Explorer" };
        revealItem.Click += (_, _) => BrowserContextReveal_Click();
        _browserTreeContextMenu.Items.Add(revealItem);
        var slideshowItem = new MenuFlyoutItem { Text = "Start slideshow from folder" };
        slideshowItem.Click += (_, _) => BrowserContextStartSlideshowFromFolder_Click();
        _browserTreeContextMenu.Items.Add(slideshowItem);
        var archiveWizardItem = new MenuFlyoutItem { Text = "Open archive wizard in this folder" };
        archiveWizardItem.Click += (_, _) => BrowserContextOpenArchiveWizardInFolder_Click();
        _browserTreeContextMenu.Items.Add(archiveWizardItem);
        var favoriteItem = new MenuFlyoutItem { Text = "Add folder to favorites" };
        favoriteItem.Click += (_, _) => BrowserContextAddFavorite_Click();
        _browserTreeContextMenu.Items.Add(favoriteItem);

        BrowserV2Host.FolderTree.ScrollViewerViewChanged += Browse2FolderTreeScroll_ViewChanged;
        BrowserV2Host.FolderTree.NavigateIntoFolderRequested += Browse2FolderTree_NavigateIntoFolderRequested;

        BrowserV2Host.FileListHeaderSortNameNatural += SortList_NameNatural_Click;
        BrowserV2Host.FileListHeaderSortSize += SortList_Size_Click;
        BrowserV2Host.FileListHeaderSortDate += SortList_Date_Click;
        BrowserV2Host.FolderListHeaderSortName += FolderBrowserHeaderSort_Name_Click;
        BrowserV2Host.FolderListHeaderSortSize += FolderBrowserHeaderSort_Size_Click;
        BrowserV2Host.FolderListHeaderSortImageCount += FolderBrowserHeaderSort_ImageCount_Click;
        BrowserV2Host.FolderListHeaderSortDate += FolderBrowserHeaderSort_Date_Click;

        BrowserV2Host.BrowserPaneContextMenuRequested -= Browse2Host_BrowserPaneContextMenuRequested;
        BrowserV2Host.BrowserPaneContextMenuRequested += Browse2Host_BrowserPaneContextMenuRequested;
    }

    private void Browse2Host_BrowserPaneContextMenuRequested(BrowserV2Host sender, BrowserPaneContextMenuRequestedEventArgs e)
    {
        if (_browserTreeContextMenu == null || e.Anchor.XamlRoot is null)
            return;

        e.Source.Handled = true;
        _browserContextMenuIsToolbarCurrentFolder = false;

        _browse2ContextMenuTargetWrapNode ??= new TreeViewNode();

        switch (e.Anchor.DataContext)
        {
            case ImagePaneRow ipr:
            {
                var path = ipr.FullPath;
                var name = Path.GetFileName(path);
                var row = new ImageRow(path, name, 0, DateTimeOffset.MinValue, "—", "—", "·");
                ApplySortFlagPresentationToRow(row, path);
                _browse2ContextMenuTargetWrapNode.Content = row;
                _browserContextMenuTargetNode = _browse2ContextMenuTargetWrapNode;
                break;
            }
            case FolderRow fr:
            {
                var label = Path.GetFileName(fr.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(label))
                    label = fr.Path;
                _browse2ContextMenuTargetWrapNode.Content = new FolderTreeEntry(fr.Path, label);
                _browserContextMenuTargetNode = _browse2ContextMenuTargetWrapNode;
                break;
            }
            default:
                return;
        }

        SyncBrowse2SyntheticPrimaryNavNode();
        _browserTreeContextMenu.ShowAt(e.Anchor, new FlyoutShowOptions { Position = e.Source.GetPosition(e.Anchor) });
    }

    private void Browse2FolderTreeScroll_ViewChanged(FolderTreeView sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate)
            return;
        if (_suppressBrowserTreeViewportMutationForColdBoot)
            return;
        SchedulePersistLayoutDebounced();
    }

    private async void Browse2FolderTree_NavigateIntoFolderRequested(FolderTreeView sender, string path)
    {
        if (string.IsNullOrEmpty(path))
            return;
        await NavigateToFolderAsync(path).ConfigureAwait(true);
    }

    internal void ScheduleViewport(BrowserTreeViewportIntent intent) =>
        _ = ScheduleViewportAsync(intent);

    internal Task ScheduleViewportAsync(BrowserTreeViewportIntent intent) =>
        Browse2ApplyViewportIntentAsync(intent);

    internal Task RunColdBootViewportAsync(BrowserTreeViewportIntent intent) =>
        Browse2ApplyViewportIntentAsync(intent);

    internal void SuppressViewportForColdBoot(bool suppress) =>
        _suppressBrowserTreeViewportMutationForColdBoot = suppress;

    private Task Browse2ApplyViewportIntentAsync(BrowserTreeViewportIntent intent)
    {
        if (_browse2Coordinator == null)
            return Task.CompletedTask;

        // Suppress incidental viewport during cold-boot restore, but always allow the explicit
        // RunColdBootViewportAsync pass (same try block still has SuppressViewportForColdBoot true).
        if (_suppressBrowserTreeViewportMutationForColdBoot
            && intent.Reason != BrowserTreeViewportReason.ColdBootRestore)
            return Task.CompletedTask;

        if (intent.Reason == BrowserTreeViewportReason.ColdBootRestore)
        {
            var anchor = _session.LastActedFsObject;
            if (!string.IsNullOrEmpty(anchor) && File.Exists(anchor))
            {
                var parent = Path.GetDirectoryName(anchor);
                if (!string.IsNullOrEmpty(parent))
                {
                    _ = _browse2Coordinator.Tree.RevealAndSelect(parent);
                    BrowserV2Host.FolderTree.ScrollFolderIntoView(parent, centerInViewport: false);
                }

                _browse2Coordinator.Images.SelectByPath(anchor);
                return Task.CompletedTask;
            }

            if (!string.IsNullOrEmpty(anchor) && Directory.Exists(anchor))
            {
                _ = _browse2Coordinator.Tree.RevealAndSelect(anchor);
                BrowserV2Host.FolderTree.ScrollFolderIntoView(anchor, centerInViewport: false);
                return Task.CompletedTask;
            }
        }

        var sel = _browse2Coordinator.Tree.Model.Selection.SelectedFolderPath;
        if (!string.IsNullOrEmpty(sel))
            BrowserV2Host.FolderTree.ScrollFolderIntoView(sel, centerInViewport: false);
        return Task.CompletedTask;
    }

    private Task RunOnUiAsync(Func<Task> work)
    {
        var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dq == null)
            return work();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dq.TryEnqueue(async () =>
            {
                try
                {
                    await work().ConfigureAwait(true);
                    tcs.TrySetResult();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }))
        {
            return work();
        }

        return tcs.Task;
    }

    private static bool IsSameOrDescendantDirectory(string ancestorOrSelfFolder, string candidateFolder)
    {
        if (string.IsNullOrEmpty(ancestorOrSelfFolder) || string.IsNullOrEmpty(candidateFolder))
            return false;
        string root;
        string cand;
        try
        {
            root = Path.GetFullPath(ancestorOrSelfFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            cand = Path.GetFullPath(candidateFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return false;
        }

        if (string.Equals(root, cand, StringComparison.OrdinalIgnoreCase))
            return true;

        return cand.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || cand.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private void SyncBrowseNavigationModeMenu()
    {
        BrowseNavModeAllItem.IsChecked = _browseNavigationMode == BrowseNavigationMode.AllImages;
        BrowseNavModeKeepItem.IsChecked = _browseNavigationMode == BrowseNavigationMode.KeepOnly;
        BrowseNavModeNotKeepItem.IsChecked = _browseNavigationMode == BrowseNavigationMode.NotKeepOnly;
        BrowseNavModeUnflaggedItem.IsChecked = _browseNavigationMode == BrowseNavigationMode.UnflaggedOnly;
        BrowseNavModeDeleteItem.IsChecked = _browseNavigationMode == BrowseNavigationMode.DeleteOnly;
    }

    internal bool TryGetSortFlagTargetPath([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? path)
    {
        var preview = _currentImageFullPath;
        var hasPreview = !string.IsNullOrEmpty(preview);

        if (_slideshowUiActive && hasPreview)
        {
            path = preview!;
            return true;
        }

        if (GetSelectedImageRow() is { } row)
        {
            if (hasPreview
                && !string.Equals(row.FullPath, preview, StringComparison.OrdinalIgnoreCase))
            {
                path = preview!;
                return true;
            }

            path = row.FullPath;
            return true;
        }

        if (hasPreview)
        {
            path = preview!;
            return true;
        }

        path = null;
        return false;
    }

    private Task StartFolderMetricsWorkAsync(string path, FolderMetricsScanScope scope, bool ignoreCache = false)
    {
        _ = scope;
        _ = ignoreCache;
        if (_browse2Coordinator != null && Directory.Exists(path))
            return _browse2Coordinator.TargetedRefresher.RefreshAsync(path, CancellationToken.None);
        return Task.CompletedTask;
    }

    private void TeardownBrowse2ListHeaderHosts()
    {
        BrowserV2Host.FolderListHeaderHost.Content = null;
        BrowserV2Host.FileListHeaderHost.Content = null;
    }

    private void SyncBrowse2ColumnHeadersAndMarkers()
    {
        ApplyLayoutFolderDetailsToFolderHeaderMarker(_browse2FolderListHeaderMarker);
        ApplyLayoutFileDetailsToHeaderMarker(_browse2FileListHeaderMarker);

        var showFolder = _layoutState.ShowBrowserFolderColumnHeadings;
        var showFile = _layoutState.ShowBrowserFileColumnHeadings;
        BrowserV2Host.SetFolderHeaderRowVisible(showFolder);
        BrowserV2Host.SetFileHeaderRowVisible(showFile);
        BrowserV2Host.FolderListHeaderHost.Content = showFolder ? _browse2FolderListHeaderMarker : null;
        BrowserV2Host.FileListHeaderHost.Content = showFile ? _browse2FileListHeaderMarker : null;
    }

    private void ApplyLayoutFileDetailsToImageRow(ImageRow row)
    {
        row.SizeDetailVisibility = _layoutState.ShowBrowserFileSize ? Visibility.Visible : Visibility.Collapsed;
        row.DateDetailVisibility = _layoutState.ShowBrowserFileDate ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyLayoutFileDetailsToHeaderMarker(BrowserFileListHeaderMarker marker)
    {
        marker.SizeHeaderVisibility = _layoutState.ShowBrowserFileSize ? Visibility.Visible : Visibility.Collapsed;
        marker.DateHeaderVisibility = _layoutState.ShowBrowserFileDate ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyLayoutFolderDetailsToFolderEntry(FolderTreeEntry row)
    {
        row.SizeDetailVisibility = _layoutState.ShowBrowserFolderSize ? Visibility.Visible : Visibility.Collapsed;
        row.ImageCountDetailVisibility = _layoutState.ShowBrowserFolderImageCount ? Visibility.Visible : Visibility.Collapsed;
        row.DateDetailVisibility = _layoutState.ShowBrowserFolderDate ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyLayoutFolderDetailsToFolderHeaderMarker(BrowserFolderListHeaderMarker marker)
    {
        marker.SizeHeaderVisibility = _layoutState.ShowBrowserFolderSize ? Visibility.Visible : Visibility.Collapsed;
        marker.ImageCountHeaderVisibility = _layoutState.ShowBrowserFolderImageCount ? Visibility.Visible : Visibility.Collapsed;
        marker.DateHeaderVisibility = _layoutState.ShowBrowserFolderDate ? Visibility.Visible : Visibility.Collapsed;
        marker.ActiveSort = _layoutState.FolderListSort;
    }

    internal void ApplyBrowserFileDetailsChrome()
    {
        if (_browse2Coordinator != null)
            _browse2Coordinator.Images.SetImageColumnVisibility(_layoutState.ShowBrowserFileSize, _layoutState.ShowBrowserFileDate);
        SyncBrowse2ColumnHeadersAndMarkers();
    }

    internal void ApplyBrowserFolderDetailsChrome()
    {
        BrowserV2Host.FolderTree.SetMetricsColumnsVisibility(
            _layoutState.ShowBrowserFolderSize,
            _layoutState.ShowBrowserFolderImageCount,
            _layoutState.ShowBrowserFolderDate);
        SyncBrowse2ColumnHeadersAndMarkers();
    }

    internal void CancelFolderResortCoalesceState()
    {
    }

    internal void ResortAllFolderGroups()
    {
    }

    internal void EnqueueFolderMetricsForAllVisibleFolderPaths()
    {
        if (_browse2Coordinator == null)
            return;
        var parent = _browse2Coordinator.Tree.Model.Selection.SelectedFolderPath;
        var root = _browse2Coordinator.Workspace.IndexRoot;
        if (string.IsNullOrEmpty(parent))
            parent = root;
        _ = _browse2Coordinator.EnsureAggregatesForVisibleChildrenAsync(parent!, CancellationToken.None);
    }

    internal void RefreshAllFolderEntrySizingDisplays() =>
        _ = Browse2RefreshVisibleFoldersAsync();

    internal void ScheduleDeferredBrowserChromeAfterStartup()
    {
        _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, DeferredBrowseShellChromeAfterStartup);
    }

    private void DeferredBrowseShellChromeAfterStartup()
    {
        ApplyBrowserFileDetailsChrome();
            _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            ApplyBrowserFolderDetailsChrome();
            _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, KickFavoriteFilesystemMapBackgroundReconcileForIndexRoots);
        });
    }

    private void UpdateBrowserToolbar()
    {
        if (string.IsNullOrEmpty(_currentFolderPath))
        {
            BrowserCurrentPathText.Text = "";
            ToolTipService.SetToolTip(BrowserCurrentPathText, null);
            BrowserNavigateUpButton.IsEnabled = false;
            return;
        }

        BrowserCurrentPathText.Text = _currentFolderPath;
        ToolTipService.SetToolTip(BrowserCurrentPathText, _currentFolderPath);
        var parent = Directory.GetParent(_currentFolderPath);
        BrowserNavigateUpButton.IsEnabled = parent != null;
    }

    private void UpdateArchiveTargetBrowserRow()
    {
        if (string.IsNullOrEmpty(_session.ArchiveRoot))
        {
            ArchiveTargetBrowserPathText.Text = "(not set)";
            ToolTipService.SetToolTip(
                ArchiveTargetBrowserRowButton,
                "Click to select archive target folder");
            return;
        }

        ArchiveTargetBrowserPathText.Text = _session.ArchiveRoot;
        ToolTipService.SetToolTip(ArchiveTargetBrowserRowButton, _session.ArchiveRoot);
    }

    private async void ArchiveTargetBrowserRow_Click(object sender, RoutedEventArgs e)
    {
        if (RootGrid.XamlRoot == null)
            return;
        await ((IPreferencesSession)this).PromptEditArchiveRootAsync(RootGrid.XamlRoot);
    }

    private TreeViewNode? TryGetBrowserTreePrimaryNavNode() => _browse2SyntheticPrimaryNavNode;

    private string? TryGetBrowseTreeSelectedFolderPath()
    {
        if (TryGetBrowserTreePrimaryNavNode()?.Content is FolderTreeEntry fe)
            return fe.Path;
        return _browse2Coordinator?.Tree.Model.Selection.SelectedFolderPath;
    }

    internal ImageRow? GetSelectedImageRow() =>
        TryGetBrowserTreePrimaryNavNode()?.Content as ImageRow;

    private BrowserPaneState BuildBrowserPaneState()
    {
        var primary = TryGetBrowserTreePrimaryNavNode();
        return new BrowserPaneState(
            _currentFolderPath,
            _browseNavAnchorPath,
            _session.LastSelectedImage,
            _currentImageFullPath,
            TryGetBrowseTreeSelectedFolderPath(),
            primary?.Content is ImageRow ir ? ir.FullPath : null);
    }

    private void SetLastActedFsObject(string? path)
    {
        _session.LastActedFsObject = path;
        SchedulePersistLayoutDebounced();
    }

    private void CaptureLastActedFsObjectFromCurrentTreeSelectionAfterWizardCommit()
    {
        var n = TryGetBrowserTreePrimaryNavNode();
        if (n?.Content is ImageRow ir)
            SetLastActedFsObject(ir.FullPath);
        else if (n?.Content is FolderTreeEntry fe && Directory.Exists(fe.Path))
            SetLastActedFsObject(fe.Path);
        else if (!string.IsNullOrEmpty(_currentImageFullPath) && File.Exists(_currentImageFullPath))
            SetLastActedFsObject(_currentImageFullPath);
        else
            SetLastActedFsObject(null);
    }

    internal void CaptureBrowserTreeSnapshotIntoSessionIfBrowsing()
    {
        if (string.IsNullOrEmpty(_currentFolderPath) || !Directory.Exists(_currentFolderPath) || _browse2Coordinator?.TreeStore is null)
        {
            _session.BrowserTree = null;
            return;
        }

        _browse2Coordinator.CaptureBrowserTreeIntoStore();
        _browse2Coordinator.TreeStore.WriteIntoSession(_session);
    }

    internal async Task NavigateToFolderAsync(
        string path,
        bool suppressViewportAfterRootPopulate = false,
        bool coldBootSessionRestore = false)
    {
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            path = Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch
        {
            return;
        }

        if (!Directory.Exists(path))
            return;

        if (coldBootSessionRestore)
            suppressViewportAfterRootPopulate = true;

        if (!coldBootSessionRestore
            && !string.IsNullOrEmpty(_currentFolderPath)
            && !string.Equals(path, _currentFolderPath, StringComparison.OrdinalIgnoreCase))
            SetLastActedFsObject(null);

        ResetPreviewUserZoom();

        if (!string.IsNullOrEmpty(_currentFolderPath)
            && string.Equals(path, _currentFolderPath, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(_pendingSelectImagePath)
            && !string.IsNullOrEmpty(_currentImageFullPath))
        {
            _pendingSelectImagePath = _currentImageFullPath;
        }

        _ = Interlocked.Increment(ref _populateBrowserGeneration);
        await SeedFavoriteFilesystemMapsIntoAggregateCachesAsync(Volatile.Read(ref _populateBrowserGeneration))
            .ConfigureAwait(true);
        _currentFolderPath = path;
        _session.LastBrowseFolder = path;
        _browseNavAnchorPath = null;
        UpdateBrowserToolbar();

        await Browse2EnsureCoordinatorForCurrentBrowseAsync().ConfigureAwait(true);

        if (_browse2Coordinator != null)
        {
            _ = _browse2Coordinator.Tree.RevealAndSelect(path);
            _browse2Coordinator.Images.CurrentFolderPath = path;
            SyncBrowse2SyntheticPrimaryNavNode();
        }

        if (!suppressViewportAfterRootPopulate && !coldBootSessionRestore)
            await ScheduleViewportAsync(BrowserTreeViewportIntentResolver.ForRootPopulate(BuildBrowserPaneState())).ConfigureAwait(true);
    }

    internal async Task RestoreColdBootBrowseAfterNavigateAsync()
    {
        if (string.IsNullOrEmpty(_currentFolderPath) || !Directory.Exists(_currentFolderPath) || _browse2Coordinator == null)
            return;

        var anchor = _session.LastActedFsObject;
        if (!string.IsNullOrEmpty(anchor) && IsAnchorPathUnderBrowseRoot(_currentFolderPath, anchor))
        {
            if (Directory.Exists(anchor))
            {
                _ = _browse2Coordinator.Tree.RevealAndSelect(anchor);
                await ApplyBrowserTreeLeadPreviewAsync().ConfigureAwait(true);
            }
            else if (File.Exists(anchor))
            {
                var dir = Path.GetDirectoryName(anchor);
                if (!string.IsNullOrEmpty(dir))
                    _ = _browse2Coordinator.Tree.RevealAndSelect(dir);
                _browse2Coordinator.Images.SelectByPath(anchor);
                await ApplyBrowserTreeLeadPreviewAsync().ConfigureAwait(true);
            }
        }

        await RunColdBootViewportAsync(BrowserTreeViewportIntentResolver.ForColdBootAnchor(BuildBrowserPaneState(), _session.LastActedFsObject))
            .ConfigureAwait(true);
    }

    private static bool IsAnchorPathUnderBrowseRoot(string browseRootFullPath, string path)
    {
        if (string.IsNullOrEmpty(browseRootFullPath) || string.IsNullOrEmpty(path))
            return false;
        try
        {
            var r = Path.GetFullPath(browseRootFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var p = Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.Equals(r, p, StringComparison.OrdinalIgnoreCase))
                return true;
            return BrowserTreeDeletePathDedupe.IsStrictDescendantPath(r, p);
        }
        catch
        {
            return false;
        }
    }

    internal async Task RefreshBrowserTreeFromSettingsAsync(string? contextMenuRowRefreshFolder = null)
    {
        Browse2ApplyImageListSortFromLayout();
        ApplyBrowserFileDetailsChrome();
        ApplyBrowserFolderDetailsChrome();
        if (_browse2Coordinator != null && !string.IsNullOrEmpty(_currentFolderPath))
            await Browse2RefreshVisibleFoldersAsync(contextMenuRowRefreshFolder).ConfigureAwait(true);
    }

    private void PrepareBrowserTreeViewportAfterWizardMutation()
    {
    }

    internal async Task ReconcileBrowserPaneAfterWizardNavigateToParentAsync(
        string parentPath,
        BrowserTreeRefocusAfterWizardContext? refocusContext)
    {
        EnterBrowserPaneMutation();
        try
        {
            if (string.IsNullOrEmpty(parentPath))
                return;

            var preferred = refocusContext?.PreferredNextFolderFullPath;
            var navigateTo = !string.IsNullOrEmpty(preferred) && Directory.Exists(preferred)
                ? preferred
                : parentPath;

            if (!Directory.Exists(navigateTo))
                return;

            await NavigateToFolderAsync(navigateTo, suppressViewportAfterRootPopulate: true).ConfigureAwait(true);
            ClearImageSelectionAndPreviewCore();
            await CommitIncrementalFolderPreviewAndSelectionAsync(refocusContext).ConfigureAwait(true);
            PrepareBrowserTreeViewportAfterWizardMutation();
            await ScheduleViewportAsync(
                    BrowserTreeViewportIntentResolver.ForWizardCommit(
                        BuildBrowserPaneState(),
                        BrowserTreeViewportReason.AfterWizardNavigateToParent,
                        refocusContext))
                .ConfigureAwait(true);
            UpdatePathOverlays();
            UpdateFullscreenMenuEnabled();
            PersistLayout();
        }
        finally
        {
            LeaveBrowserPaneMutation();
        }
    }

    private static BrowserTreeRefocusAfterWizardContext? MergeRefocusContextWithDeleteStats(
        BrowserTreeRefocusAfterWizardContext? refocusContext,
        IReadOnlyList<WizardPredeletedFileStat> succeeded,
        IReadOnlyList<string>? imagePanePathsBeforeDeletion)
    {
        if (succeeded.Count == 0)
            return refocusContext;

        var deleted = succeeded.Select(s => s.FullPath).ToList();
        var c = refocusContext;
        return new BrowserTreeRefocusAfterWizardContext(
            c?.PreferredNextFolderFullPath,
            c?.ImageDeletionWorkingFolder,
            c?.DeletedImagePathsForRefocus ?? deleted,
            c?.ImagePanePathsBeforeDeletion ?? imagePanePathsBeforeDeletion);
    }

    internal async Task RefreshBrowserPaneAfterWizardImageDeletesAsync(
        IReadOnlyList<WizardPredeletedFileStat> succeeded,
        BrowserTreeRefocusAfterWizardContext? refocusContext = null)
    {
        EnterBrowserPaneMutation();
        try
        {
            _ = Interlocked.Increment(ref _populateBrowserGeneration);

            IReadOnlyList<string>? paneOrderBefore = null;
            if (_browse2Coordinator != null && succeeded.Count > 0)
                paneOrderBefore = _browse2Coordinator.Images.Items.Select(r => r.FullPath).ToList();

            var refocusForCommit = MergeRefocusContextWithDeleteStats(refocusContext, succeeded, paneOrderBefore);

            var affectedParentDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (succeeded.Count > 0)
            {
                var mapPurge = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in succeeded)
                {
                    foreach (var a in CollectAncestorDirectoryPrefixesForFavoriteMapPurge(s.FullPath))
                        mapPurge.Add(a);
                    var parent = Path.GetDirectoryName(s.FullPath);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        try
                        {
                            affectedParentDirs.Add(Path.GetFullPath(parent));
                        }
                        catch
                        {
                            affectedParentDirs.Add(parent);
                        }
                    }
                }

                await PurgeFavoriteFilesystemMapsForPrefixesAsync(mapPurge).ConfigureAwait(true);
                await Browse2NotifyWizardImageDeletesAsync(succeeded).ConfigureAwait(true);
            }

            foreach (var d in affectedParentDirs)
            {
                if (_browse2Coordinator != null && Directory.Exists(d))
                    await _browse2Coordinator.RefreshFolderListingAsync(d, CancellationToken.None).ConfigureAwait(true);
            }

            await CommitIncrementalFolderPreviewAndSelectionAsync(refocusForCommit).ConfigureAwait(true);
            PrepareBrowserTreeViewportAfterWizardMutation();
            await ScheduleViewportAsync(
                    BrowserTreeViewportIntentResolver.ForWizardCommit(
                        BuildBrowserPaneState(),
                        refocusForCommit))
                .ConfigureAwait(true);
            UpdatePathOverlays();
            UpdateFullscreenMenuEnabled();
            PersistLayout();
        }
        finally
        {
            LeaveBrowserPaneMutation();
        }
    }

    internal async Task RefreshBrowserPaneAfterWizardUndoAsync(IReadOnlyList<string> restoredPaths)
    {
        EnterBrowserPaneMutation();
        try
        {
            _ = Interlocked.Increment(ref _populateBrowserGeneration);

            var affectedParentDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in restoredPaths)
            {
                var parent = Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(parent))
                {
                    try
                    {
                        affectedParentDirs.Add(Path.GetFullPath(parent));
                    }
                    catch
                    {
                        affectedParentDirs.Add(parent);
                    }
                }
            }

            var mapPurgeUndo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in restoredPaths)
            {
                foreach (var a in CollectAncestorDirectoryPrefixesForFavoriteMapPurge(p))
                    mapPurgeUndo.Add(a);
            }

            await PurgeFavoriteFilesystemMapsForPrefixesAsync(mapPurgeUndo).ConfigureAwait(true);

            foreach (var d in affectedParentDirs)
            {
                if (_browse2Coordinator != null && Directory.Exists(d))
                    await _browse2Coordinator.RefreshFolderListingAsync(d, CancellationToken.None).ConfigureAwait(true);
            }

            await CommitIncrementalFolderPreviewAndSelectionAsync(null).ConfigureAwait(true);
            PrepareBrowserTreeViewportAfterWizardMutation();
            await ScheduleViewportAsync(
                    BrowserTreeViewportIntentResolver.ForWizardCommit(
                        BuildBrowserPaneState(),
                        BrowserTreeViewportReason.AfterWizardUndo,
                        null))
                .ConfigureAwait(true);
            UpdatePathOverlays();
            UpdateFullscreenMenuEnabled();
            PersistLayout();
        }
        finally
        {
            LeaveBrowserPaneMutation();
        }
    }

    internal async Task SynchronizeCurrentFolderImageNodesWithDiskAsync(bool forceStructuralImageRebuild = false)
    {
        if (string.IsNullOrEmpty(_currentFolderPath) || !Directory.Exists(_currentFolderPath) || _browse2Coordinator == null)
            return;
        await _browse2Coordinator.RefreshFolderListingAsync(_currentFolderPath, CancellationToken.None).ConfigureAwait(true);
        _browse2Coordinator.Images.RequestReload();
        _ = forceStructuralImageRebuild;
    }

    internal bool TryRemoveFolderTreeNodeByPath(string folderFullPath)
    {
        if (_browse2Coordinator == null || string.IsNullOrEmpty(folderFullPath))
            return false;
        _ = _browse2Coordinator.TargetedRefresher.RefreshAsync(folderFullPath, CancellationToken.None);
        var parent = Directory.GetParent(folderFullPath)?.FullName;
        if (!string.IsNullOrEmpty(parent))
            _ = _browse2Coordinator.TargetedRefresher.RefreshAsync(parent, CancellationToken.None);
        return true;
    }

    internal void Browse2NavigateClearRootSnapshot()
    {
        Browse2DisposeSession();
        _session.BrowserTree = null;
    }

    private async Task CommitIncrementalFolderPreviewAndSelectionAsync(
        BrowserTreeRefocusAfterWizardContext? refocusContext)
    {
        try
        {
            if (_browse2Coordinator == null)
            {
                ClearImageSelectionAndPreviewCore();
                return;
            }

            if (!string.IsNullOrEmpty(_currentImageFullPath) && File.Exists(_currentImageFullPath))
            {
                var dir = Path.GetDirectoryName(_currentImageFullPath);
                if (!string.IsNullOrEmpty(dir))
                    _ = _browse2Coordinator.Tree.RevealAndSelect(dir);
                _browse2Coordinator.Images.SelectByPath(_currentImageFullPath);
                await ApplyBrowserTreeLeadPreviewAsync().ConfigureAwait(true);
                return;
            }

            var preferred = refocusContext?.PreferredNextFolderFullPath;
            if (!string.IsNullOrEmpty(preferred) && Directory.Exists(preferred))
            {
                _ = _browse2Coordinator.Tree.RevealAndSelect(preferred);
                _browse2Coordinator.Images.CurrentFolderPath = preferred;
                await _browse2Coordinator.Images.WaitForReloadAppliedAsync(CancellationToken.None).ConfigureAwait(true);
                foreach (var it in _browse2Coordinator.Images.Items)
                {
                    if (!BrowseNavigationModeFilter.Matches(_sortSession.GetState(it.FullPath), _browseNavigationMode))
                        continue;
                    _browse2Coordinator.Images.SelectByPath(it.FullPath);
                    EnqueuePreviewNavigation(it.FullPath, false);
                    _session.LastSelectedImage = it.FullPath;
                    await ApplyBrowserTreeLeadPreviewAsync().ConfigureAwait(true);
                    return;
                }
            }

            if (refocusContext is { } rcDel
                && !string.IsNullOrEmpty(rcDel.ImageDeletionWorkingFolder)
                && Directory.Exists(rcDel.ImageDeletionWorkingFolder)
                && !string.IsNullOrEmpty(_currentFolderPath)
                && IsSameOrDescendantDirectory(_currentFolderPath, rcDel.ImageDeletionWorkingFolder))
            {
                _ = _browse2Coordinator.Tree.RevealAndSelect(_currentFolderPath);
                _browse2Coordinator.Images.CurrentFolderPath = _currentFolderPath;
                await _browse2Coordinator.Images.WaitForReloadAppliedAsync(CancellationToken.None).ConfigureAwait(true);

                var before = rcDel.ImagePanePathsBeforeDeletion;
                var deleted = rcDel.DeletedImagePathsForRefocus;
                string? pick = null;
                if (before is { Count: > 0 } && deleted is { Count: > 0 })
                    pick = BrowseContextImageSequence.PickNextDisplayedPathAfterRemovalsInOrderedList(before, deleted);

                if (!string.IsNullOrEmpty(pick) && File.Exists(pick))
                {
                    var pickDir = Path.GetDirectoryName(pick);
                    if (!string.IsNullOrEmpty(pickDir))
                    {
                        _ = _browse2Coordinator.Tree.RevealAndSelect(pickDir);
                        _browse2Coordinator.Images.CurrentFolderPath = pickDir;
                        await _browse2Coordinator.Images.WaitForReloadAppliedAsync(CancellationToken.None).ConfigureAwait(true);
                    }

                    _browse2Coordinator.Images.SelectByPath(pick);
                    EnqueuePreviewNavigation(pick, false);
                    _session.LastSelectedImage = pick;
                    await ApplyBrowserTreeLeadPreviewAsync().ConfigureAwait(true);
                    return;
                }

                foreach (var it in _browse2Coordinator.Images.Items)
                {
                    if (!BrowseNavigationModeFilter.Matches(_sortSession.GetState(it.FullPath), _browseNavigationMode))
                        continue;
                    _browse2Coordinator.Images.SelectByPath(it.FullPath);
                    EnqueuePreviewNavigation(it.FullPath, false);
                    _session.LastSelectedImage = it.FullPath;
                    await ApplyBrowserTreeLeadPreviewAsync().ConfigureAwait(true);
                    return;
                }

                ClearBrowserPaneImagePreviewStatePreserveTreeSelection();
                return;
            }

            if (!string.IsNullOrEmpty(_currentFolderPath) && Directory.Exists(_currentFolderPath))
            {
                _ = _browse2Coordinator.Tree.RevealAndSelect(_currentFolderPath);
                _browse2Coordinator.Images.CurrentFolderPath = _currentFolderPath;
                await _browse2Coordinator.Images.WaitForReloadAppliedAsync(CancellationToken.None).ConfigureAwait(true);
                foreach (var it in _browse2Coordinator.Images.Items)
                {
                    if (!BrowseNavigationModeFilter.Matches(_sortSession.GetState(it.FullPath), _browseNavigationMode))
                        continue;
                    _browse2Coordinator.Images.SelectByPath(it.FullPath);
                    EnqueuePreviewNavigation(it.FullPath, false);
                    _session.LastSelectedImage = it.FullPath;
                    await ApplyBrowserTreeLeadPreviewAsync().ConfigureAwait(true);
                    return;
                }
            }

            ClearImageSelectionAndPreviewCore();
        }
        finally
        {
            CaptureLastActedFsObjectFromCurrentTreeSelectionAfterWizardCommit();
        }
    }

    internal void ClearDeferredWizardBatchBrowserRefreshCapture()
    {
        _deferredWizardBatchSucceededStats = null;
        _deferredWizardBatchRefocusContext = null;
    }

    private void SyncBrowseTreeSelection(TreeViewNode node)
    {
        if (node.Content is ImageRow ir)
            _browse2Coordinator?.Images.SelectByPath(ir.FullPath);
        else if (node.Content is FolderTreeEntry fe)
            _browse2Coordinator?.Tree.SetSelectedFolder(fe.Path);
        SyncBrowse2SyntheticPrimaryNavNode();
    }

    private async Task ApplyBrowserTreeLeadPreviewAsync()
    {
        if (_browse2Coordinator?.Images.SelectedImagePath is { } imgPath && File.Exists(imgPath))
        {
            _browseNavAnchorPath = imgPath;
            var name = Path.GetFileName(imgPath);
            var row = new ImageRow(imgPath, name, 0, DateTimeOffset.MinValue, "—", "—", "·");
            ApplySortFlagPresentationToRow(row, imgPath);
            await OnFolderTreeImageRowSelectedAsync(row).ConfigureAwait(true);
        }
        else
        {
            _browseNavAnchorPath = null;
            if (!_isFullscreen && !string.IsNullOrEmpty(_currentImageFullPath))
                ClearBrowserPaneImagePreviewStatePreserveTreeSelection();
            var folder = _browse2Coordinator?.Tree.Model.Selection.SelectedFolderPath;
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                SetLastActedFsObject(folder);
        }

        UpdateFullscreenMenuEnabled();
    }

    private void ClearBrowserPaneImagePreviewStatePreserveTreeSelection()
    {
        DiscardSlideshowSession();
        PreviewImage.Source = null;
        FullscreenImage.Source = null;
        _currentImageFullPath = null;
        InvalidatePreviewRequestsAndClearQueue();
        _lastDecodeTargetBoxWidthPx = -1;
        _lastDecodeTargetBoxHeightPx = -1;
        ClearPreviewBitmapPixelSize();
        UpdatePreviewScrollMetrics();
        UpdatePathOverlays();
        PersistLayout();
    }

    internal void ClearImageSelectionAndPreview()
    {
        _browse2Coordinator?.Images.SelectByPath(null);
        ClearImageSelectionAndPreviewCore();
    }

    internal void ClearImageSelectionAndPreviewCore()
    {
        _browse2Coordinator?.Images.SelectByPath(null);
        ClearBrowserPaneImagePreviewStatePreserveTreeSelection();
    }

    private void PruneBrowserTreeSelectionToVisibleNavRows()
    {
    }

    private async void BrowserNavigateUp_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentFolderPath))
            return;
        var parent = Directory.GetParent(_currentFolderPath);
        if (parent == null)
            return;
        await NavigateToFolderAsync(parent.FullName).ConfigureAwait(true);
    }

    private void BrowseNavigateByStep(BrowseNavStepKind step)
    {
        if (IsBrowserPaneMutationInProgress)
            return;
        IncrementNavCommandCounter();
        _mouseButtonClickChainTracker.Reset();
        _ = BrowseNavigateByStepAsync(step);
    }

    private async Task BrowseNavigateByStepAsync(BrowseNavStepKind step)
    {
        if (IsBrowserPaneMutationInProgress || _browse2Coordinator == null)
            return;

        var sel = TryGetBrowserTreePrimaryNavNode();
        var selPath = sel?.Content is ImageRow sr ? sr.FullPath : null;
        var contextDir = BrowseContextDirectory.Resolve(
            _browseNavAnchorPath,
            selPath,
            _currentImageFullPath,
            _currentFolderPath,
            TryGetBrowseTreeSelectedFolderPath());
        if (string.IsNullOrEmpty(contextDir))
            return;

        var paths = await GetBrowseNavigationPathsForContextAsync(contextDir).ConfigureAwait(true);
        if (paths.Count == 0)
            return;

        var i = BrowseSequentialNavIndex.ComputeTargetIndexForStep(
            paths,
            _browseNavAnchorPath,
            selPath,
            _currentImageFullPath,
            step);
        if (i < 0)
            return;

        _browseNavAnchorPath = paths[i];
        var path = paths[i];

        EnqueuePreviewNavigation(path, false);
        SetLastActedFsObject(path);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            _ = _browse2Coordinator.Tree.RevealAndSelect(dir);
        _browse2Coordinator.Images.SelectByPath(path);
        SyncBrowse2SyntheticPrimaryNavNode();
        ScheduleViewport(BrowserTreeViewportIntentResolver.ForImageStep(BuildBrowserPaneState(), path));
    }

    private List<string> BuildBrowseNavPathsFromBrowse2Items(string contextDir)
    {
        if (_browse2Coordinator == null || string.IsNullOrEmpty(_currentFolderPath))
            return new List<string>();

        if (SameDirectoryPath(contextDir, _currentFolderPath))
            return BuildFilteredBrowseNavPathsFromPaths(_browse2Coordinator.Images.Items.Select(r => r.FullPath).ToList());

        var list = new List<string>();
        foreach (var it in _browse2Coordinator.Images.Items)
        {
            var d = Path.GetDirectoryName(it.FullPath);
            if (string.IsNullOrEmpty(d))
                continue;
            if (SameDirectoryPath(d, contextDir))
                list.Add(it.FullPath);
        }

        return BuildFilteredBrowseNavPathsFromPaths(list);
    }

    private List<string> BuildFilteredBrowseNavPathsFromPaths(List<string> paths)
    {
        if (_browseNavigationMode == BrowseNavigationMode.AllImages)
            return paths;

        var filtered = new List<string>();
        foreach (var p in paths)
        {
            if (BrowseNavigationModeFilter.Matches(_sortSession.GetState(p), _browseNavigationMode))
                filtered.Add(p);
        }

        return filtered;
    }

    private async Task<List<string>> GetBrowseNavigationPathsForContextAsync(string contextDir)
    {
        var fromTree = BuildBrowseNavPathsFromBrowse2Items(contextDir);
        if (fromTree.Count > 0)
            return DedupePathsPreserveOrder(fromTree);

        if (string.IsNullOrEmpty(_currentFolderPath)
            || string.IsNullOrEmpty(contextDir)
            || !Directory.Exists(contextDir)
            || !BrowseContextImageSequence.IsContextDirectoryUnderBrowseRoot(_currentFolderPath, contextDir))
            return new List<string>();

        IReadOnlyList<ImageHoard.Core.Models.FileSystemEntry> entries;
        try
        {
            entries = await AppServices.FileSystem.ListDirectoryAsync(contextDir).ConfigureAwait(false);
        }
        catch
        {
            return new List<string>();
        }

        var imageFiles = BrowseContextImageSequence.PickImmediateImageFiles(entries);
        var sortKind = MapListSortKind(_layoutState.ListSort);
        var ordered = BrowseContextImageSequence.OrderImageFileEntries(imageFiles, sortKind);
        return BrowseContextImageSequence.FilterPathsByNavigationMode(
            ordered,
            _browseNavigationMode,
            p => _sortSession.GetState(p));
    }

    private static List<string> DedupePathsPreserveOrder(List<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var r = new List<string>(paths.Count);
        foreach (var p in paths)
        {
            if (seen.Add(p))
                r.Add(p);
        }

        return r;
    }

    private static BrowseImageListSortKind MapListSortKind(ListSortKind kind) =>
        kind switch
        {
            ListSortKind.NameNatural => BrowseImageListSortKind.NameNatural,
            ListSortKind.Name => BrowseImageListSortKind.Name,
            ListSortKind.DateModified => BrowseImageListSortKind.DateModified,
            ListSortKind.Size => BrowseImageListSortKind.Size,
            _ => BrowseImageListSortKind.NameNatural,
        };

    private async Task TrySyncBrowseTreeSelectionToImagePathAsync(string imagePath)
    {
        if (_browse2Coordinator == null)
            return;
        var dir = Path.GetDirectoryName(imagePath);
        if (string.IsNullOrEmpty(dir))
            return;
        _ = _browse2Coordinator.Tree.RevealAndSelect(dir);
        _browse2Coordinator.Images.CurrentFolderPath = dir;
        _browse2Coordinator.Images.SelectByPath(imagePath);
        SyncBrowse2SyntheticPrimaryNavNode();
        await Task.Yield();
    }

    internal void BrowseNavigateSiblingFolderFromInput(int delta, string? contextDirectoryOverride = null)
    {
        if (IsBrowserPaneMutationInProgress)
            return;
        _ = BrowseNavigateSiblingFolderAsync(delta, contextDirectoryOverride);
    }

    private async Task BrowseNavigateSiblingFolderAsync(int delta, string? contextDirectoryOverride = null)
    {
        if (delta != 1 && delta != -1 || IsBrowserPaneMutationInProgress || _browse2Coordinator == null)
            return;

        string? contextDir;
        if (!string.IsNullOrEmpty(contextDirectoryOverride))
        {
            string normalized;
            try
            {
                normalized = Path.GetFullPath(contextDirectoryOverride);
            }
            catch
            {
                normalized = contextDirectoryOverride;
            }

            if (string.IsNullOrEmpty(_currentFolderPath)
                || !BrowseContextImageSequence.IsContextDirectoryUnderBrowseRoot(_currentFolderPath, normalized))
                return;

            contextDir = normalized;
        }
        else
        {
            var sel = TryGetBrowserTreePrimaryNavNode();
            var selPath = sel?.Content is ImageRow sr ? sr.FullPath : null;
            contextDir = BrowseContextDirectory.Resolve(
                _browseNavAnchorPath,
                selPath,
                _currentImageFullPath,
                _currentFolderPath,
                TryGetBrowseTreeSelectedFolderPath());
        }

        if (string.IsNullOrEmpty(contextDir))
            return;

        var parent = Directory.GetParent(contextDir);
        if (parent == null)
            return;

        IReadOnlyList<ImageHoard.Core.Models.FileSystemEntry> entries;
        try
        {
            // Must resume on the UI thread: sibling navigation calls TreeController.RevealAndSelect (dispatcher-only).
            entries = await AppServices.FileSystem.ListDirectoryAsync(parent.FullName).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetTransientStatus(ex.Message);
            return;
        }

        var dirs = FolderDirectorySort.SortDirectories(
            entries.Where(e => e.IsDirectory),
            _layoutState.FolderListSort,
            _folderAggregateBytesByPath,
            _folderImageFileCountByPath);

        if (dirs.Count == 0)
            return;

        string currentNorm;
        try
        {
            currentNorm = Path.GetFullPath(contextDir);
        }
        catch
        {
            currentNorm = contextDir;
        }

        var idx = -1;
        for (var i = 0; i < dirs.Count; i++)
        {
            string dPath;
            try
            {
                dPath = Path.GetFullPath(dirs[i].FullPath);
            }
            catch
            {
                dPath = dirs[i].FullPath;
            }

            if (string.Equals(dPath, currentNorm, StringComparison.OrdinalIgnoreCase))
            {
                idx = i;
                break;
            }
        }

        if (idx < 0)
            return;

        var nextIdx = delta > 0 ? idx + 1 : idx - 1;
        if (nextIdx < 0 || nextIdx >= dirs.Count)
            return;

        var targetFolderPath = dirs[nextIdx].FullPath;

        var contextIsBrowseRoot = !string.IsNullOrEmpty(_currentFolderPath)
            && SameDirectoryPath(contextDir, _currentFolderPath);

        if (contextIsBrowseRoot)
        {
            await NavigateToFolderAsync(targetFolderPath).ConfigureAwait(true);
            if (_browse2Coordinator == null)
                return;
            await _browse2Coordinator.Images.WaitForReloadAppliedAsync(CancellationToken.None).ConfigureAwait(true);
            var first = _browse2Coordinator.Images.Items.FirstOrDefault();
            if (first != null
                && BrowseNavigationModeFilter.Matches(_sortSession.GetState(first.FullPath), _browseNavigationMode))
            {
                _browse2Coordinator.Images.SelectByPath(first.FullPath);
                EnqueuePreviewNavigation(first.FullPath, false);
                return;
            }

            SetTransientStatus("No images found in that folder (or none match the current browse filter).");
            ClearImageSelectionAndPreviewCore();
            return;
        }

        _ = _browse2Coordinator.Tree.RevealAndSelect(targetFolderPath);
        _browse2Coordinator.Images.CurrentFolderPath = targetFolderPath;
        await _browse2Coordinator.Images.WaitForReloadAppliedAsync(CancellationToken.None).ConfigureAwait(true);
        var pick = _browse2Coordinator.Images.Items.FirstOrDefault();
        if (pick != null)
        {
            _browse2Coordinator.Images.SelectByPath(pick.FullPath);
            EnqueuePreviewNavigation(pick.FullPath, false);
        }

        ScheduleViewport(BrowserTreeViewportIntentResolver.ForSiblingFolderNav(BuildBrowserPaneState(), targetFolderPath));
    }

    private static bool SameDirectoryPath(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;
        try
        {
            return string.Equals(
                Path.GetFullPath(a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                Path.GetFullPath(b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal void CycleBrowseNavigationModeFromInput()
    {
        _browseNavigationMode = BrowseNavigationModeFilter.CycleNext(_browseNavigationMode);
        SyncBrowseNavigationModeMenu();
        EnsurePreviewMatchesBrowseNavigationMode();
        UpdatePathOverlays();
    }

    internal void SetBrowseNavigationMode(BrowseNavigationMode mode)
    {
        _browseNavigationMode = mode;
        if (_browse2Coordinator != null)
            _browse2Coordinator.Images.NavigationMode = mode;
        SyncBrowseNavigationModeMenu();
        EnsurePreviewMatchesBrowseNavigationMode();
        UpdatePathOverlays();
    }

    private void EnsurePreviewMatchesBrowseNavigationMode() =>
        _ = EnsurePreviewMatchesBrowseNavigationModeAsync();

    private async Task EnsurePreviewMatchesBrowseNavigationModeAsync()
    {
        if (_browseNavigationMode == BrowseNavigationMode.AllImages)
            return;
        var path = _currentImageFullPath;
        if (string.IsNullOrEmpty(path))
            return;
        if (BrowseNavigationModeFilter.Matches(_sortSession.GetState(path), _browseNavigationMode))
            return;

        var sel = TryGetBrowserTreePrimaryNavNode();
        var selPath = sel?.Content is ImageRow sr ? sr.FullPath : null;
        var contextDir = BrowseContextDirectory.Resolve(
            _browseNavAnchorPath,
            selPath,
            path,
            _currentFolderPath,
            TryGetBrowseTreeSelectedFolderPath());
        if (string.IsNullOrEmpty(contextDir))
        {
            ClearImageSelectionAndPreview();
            return;
        }

        var paths = await GetBrowseNavigationPathsForContextAsync(contextDir).ConfigureAwait(true);
        if (paths.Count == 0)
        {
            ClearImageSelectionAndPreview();
            return;
        }

        var firstPath = paths[0];
        EnqueuePreviewNavigation(firstPath, false);
        await TrySyncBrowseTreeSelectionToImagePathAsync(firstPath).ConfigureAwait(true);
    }

    private void BrowseTreeKeyboardMoveSelection(int delta)
    {
        if (_browse2Coordinator == null)
            return;

        var sel = TryGetBrowserTreePrimaryNavNode();
        if (sel?.Content is ImageRow)
        {
            BrowseNavigateByStep(delta > 0 ? BrowseNavStepKind.Next : BrowseNavStepKind.Previous);
            return;
        }

        if (sel?.Content is FolderTreeEntry fe)
        {
            string path;
            try
            {
                path = Path.GetFullPath(fe.Path);
            }
            catch
            {
                path = fe.Path;
            }

            if (!string.IsNullOrEmpty(_currentFolderPath)
                && BrowseContextImageSequence.IsContextDirectoryUnderBrowseRoot(_currentFolderPath, path))
                BrowseNavigateSiblingFolderFromInput(delta, path);

            return;
        }

        var rows = _browse2Coordinator.Tree.Model.Rows;
        var cur = _browse2Coordinator.Tree.Model.Selection.SelectedFolderPath;
        var idx = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            if (string.Equals(rows[i].Path, cur, StringComparison.OrdinalIgnoreCase))
            {
                idx = i;
                break;
            }
        }

        if (idx < 0)
            return;
        var next = Math.Clamp(idx + delta, 0, rows.Count - 1);
        if (next == idx)
            return;
        _browse2Coordinator.Tree.SetSelectedFolder(rows[next].Path);
        var moveTargetPath = rows[next].Path;
        ScheduleViewport(BrowserTreeViewportIntentResolver.ForKeyboardMove(BuildBrowserPaneState(), moveTargetPath));
        SetLastActedFsObject(moveTargetPath);
    }

    private void BrowseTreeKeyboardExpandFolderTarget()
    {
        if (_browse2Coordinator == null)
            return;
        var p = _browse2Coordinator.Tree.Model.Selection.SelectedFolderPath;
        if (string.IsNullOrEmpty(p))
            return;
        if (!_browse2Coordinator.Workspace.TryGetEntry(p, out var e) || !e.HasSubfolders)
            return;
        if (!_browse2Coordinator.Tree.Model.Expansion.Contains(p))
            _ = _browse2Coordinator.Tree.ExpandFolder(p);
    }

    private void BrowseTreeKeyboardCollapseFolderTarget()
    {
        if (_browse2Coordinator == null)
            return;
        var p = _browse2Coordinator.Tree.Model.Selection.SelectedFolderPath;
        if (string.IsNullOrEmpty(p))
            return;
        if (_browse2Coordinator.Tree.Model.Expansion.Contains(p))
            _ = _browse2Coordinator.Tree.CollapseFolder(p);
    }

    private bool IsFocusInsideBrowserTree()
    {
        if (RootGrid.XamlRoot == null)
            return false;
        var focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot) as DependencyObject;
        return IsDescendantOf(focused, BrowserV2Host.FolderTree) || IsDescendantOf(focused, BrowserV2Host.ImagePane);
    }

    private bool TryBeginRenameSelectedBrowserItem()
    {
        var img = _browse2Coordinator?.Images.SelectedImagePath;
        if (!string.IsNullOrEmpty(img) && File.Exists(img))
        {
            _ = PromptRenameImagePathAsync(img);
            return true;
        }

        var folder = _browse2Coordinator?.Tree.Model.Selection.SelectedFolderPath;
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            _ = PromptRenameFolderPathAsync(folder);
            return true;
        }

        return false;
    }

    internal bool TryBeginRenameFolderByPath(string folderFullPath)
    {
        if (string.IsNullOrEmpty(folderFullPath) || !Directory.Exists(folderFullPath))
            return false;
        _ = PromptRenameFolderPathAsync(folderFullPath);
        return true;
    }

    private async Task PromptRenameImagePathAsync(string fullPath)
    {
        var name = Path.GetFileName(fullPath);
        var box = new TextBox { Text = name, Width = 400 };
        var dlg = new ContentDialog
        {
            Title = "Rename file",
            Content = box,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary)
            return;
        var row = new ImageRow(fullPath, name, 0, DateTimeOffset.MinValue, "—", "—", "·");
        await CommitImageRenameAsync(row, box.Text).ConfigureAwait(true);
    }

    private async Task PromptRenameFolderPathAsync(string folderPath)
    {
        var name = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(name))
            return;
        var box = new TextBox { Text = name, Width = 400 };
        var dlg = new ContentDialog
        {
            Title = "Rename folder",
            Content = box,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary)
            return;
        var fe = new FolderTreeEntry(folderPath, name);
        await CommitFolderRenameBrowse2Async(fe, box.Text).ConfigureAwait(true);
    }

    private async Task CommitImageRenameAsync(ImageRow row, string newNameInput)
    {
        var trimmed = newNameInput.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            SetTransientStatus("Name cannot be empty.");
            return;
        }

        var oldPath = row.FullPath;
        var dir = Path.GetDirectoryName(oldPath);
        if (string.IsNullOrEmpty(dir))
            return;

        var ext = Path.GetExtension(oldPath);
        var desired = trimmed.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + ext;

        string destPath;
        try
        {
            destPath = BrowserPaneRenameHelper.PickUniqueFileName(dir, desired, oldPath);
        }
        catch (Exception ex)
        {
            SetTransientStatus(ex.Message);
            return;
        }

        if (string.Equals(destPath, oldPath, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            File.Move(oldPath, destPath);
        }
        catch (Exception ex)
        {
            SetTransientStatus("Rename failed: " + ex.Message);
            return;
        }

        if (string.Equals(_session.LastSelectedImage, oldPath, StringComparison.OrdinalIgnoreCase))
            _session.LastSelectedImage = destPath;
        if (string.Equals(_session.LastActedFsObject, oldPath, StringComparison.OrdinalIgnoreCase))
            _session.LastActedFsObject = destPath;
        if (string.Equals(_currentImageFullPath, oldPath, StringComparison.OrdinalIgnoreCase))
            _currentImageFullPath = destPath;
        if (string.Equals(_browseNavAnchorPath, oldPath, StringComparison.OrdinalIgnoreCase))
            _browseNavAnchorPath = destPath;

        _browse2Coordinator?.Images.SelectByPath(destPath);
        await RefreshBrowserTreeFromSettingsAsync().ConfigureAwait(true);
        UpdatePathOverlays();
        PersistLayout();
        SetTransientStatus("Renamed.");
    }

    private async Task CommitFolderRenameBrowse2Async(FolderTreeEntry folderEntry, string newNameInput)
    {
        var trimmed = newNameInput.TrimEnd();
        if (string.IsNullOrEmpty(trimmed))
        {
            SetTransientStatus("Name cannot be empty.");
            return;
        }

        var oldPath = folderEntry.Path;
        var parent = Path.GetDirectoryName(oldPath);
        if (string.IsNullOrEmpty(parent))
            return;

        string destPath;
        try
        {
            destPath = BrowserPaneRenameHelper.PickUniqueDirectoryName(parent, trimmed, oldPath);
        }
        catch (Exception ex)
        {
            SetTransientStatus(ex.Message);
            return;
        }

        if (string.Equals(destPath, oldPath, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            Directory.Move(oldPath, destPath);
        }
        catch (Exception ex)
        {
            SetTransientStatus("Rename failed: " + ex.Message);
            return;
        }

        if (!string.IsNullOrEmpty(_currentFolderPath)
            && string.Equals(_currentFolderPath, oldPath, StringComparison.OrdinalIgnoreCase))
        {
            _currentFolderPath = destPath;
            _session.LastBrowseFolder = destPath;
        }

        await Browse2NotifyWizardDirectoryMovedAsync(oldPath, destPath).ConfigureAwait(true);
        await NavigateToFolderAsync(_currentFolderPath ?? destPath).ConfigureAwait(true);
        UpdatePathOverlays();
        PersistLayout();
        SetTransientStatus("Renamed folder.");
    }

    private async void BrowserBrowseToolbar_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentFolderPath) || !Directory.Exists(_currentFolderPath))
            return;

        e.Handled = true;
        _browserContextMenuIsToolbarCurrentFolder = true;
        _browserContextMenuTargetNode = null;
        if (sender is not FrameworkElement anchor || _browserTreeContextMenu == null)
            return;
        var options = new FlyoutShowOptions { Position = e.GetPosition(anchor) };
        _browserTreeContextMenu.ShowAt(anchor, options);
        await Task.CompletedTask;
    }

    private async void BrowserContextRefresh_Click()
    {
        if (_browserContextMenuIsToolbarCurrentFolder)
        {
            _browserContextMenuIsToolbarCurrentFolder = false;
            await RefreshBrowserTreeFromSettingsAsync(contextMenuRowRefreshFolder: null).ConfigureAwait(true);
            return;
        }

        var rowFolder = _browserContextMenuTargetNode?.Content switch
        {
            FolderTreeEntry fe => fe.Path,
            ImageRow row => Path.GetDirectoryName(row.FullPath),
            _ => null,
        };

        await RefreshBrowserTreeFromSettingsAsync(contextMenuRowRefreshFolder: rowFolder).ConfigureAwait(true);
    }

    private void BrowserContextRename_Click()
    {
        if (_browserContextMenuIsToolbarCurrentFolder)
        {
            _browserContextMenuIsToolbarCurrentFolder = false;
            _ = PromptRenameFolderPathAsync(_currentFolderPath ?? string.Empty);
            return;
        }

        TryBeginRenameSelectedBrowserItem();
    }

    private void BrowserContextReveal_Click()
    {
        if (_browserContextMenuIsToolbarCurrentFolder)
        {
            _browserContextMenuIsToolbarCurrentFolder = false;
            if (!string.IsNullOrEmpty(_currentFolderPath) && Directory.Exists(_currentFolderPath))
                TryRevealPathInExplorer(_currentFolderPath, isDirectory: true);
            return;
        }

        if (_browserContextMenuTargetNode?.Content is ImageRow row)
            TryRevealPathInExplorer(row.FullPath, isDirectory: false);
        else if (_browserContextMenuTargetNode?.Content is FolderTreeEntry fe)
            TryRevealPathInExplorer(fe.Path, isDirectory: true);
    }

    private void BrowserContextAddFavorite_Click()
    {
        if (_browserContextMenuIsToolbarCurrentFolder)
        {
            _browserContextMenuIsToolbarCurrentFolder = false;
            if (!string.IsNullOrEmpty(_currentFolderPath))
                AddFolderToFavorites(_currentFolderPath);
            return;
        }

        var folder = ResolveFolderPathForFavoriteFromNode(_browserContextMenuTargetNode);
        if (!string.IsNullOrEmpty(folder))
            AddFolderToFavorites(folder);
    }

    private async void BrowserContextStartSlideshowFromFolder_Click()
    {
        string? root;
        if (_browserContextMenuIsToolbarCurrentFolder)
        {
            _browserContextMenuIsToolbarCurrentFolder = false;
            root = _currentFolderPath;
        }
        else
            root = ResolveFolderPathForFavoriteFromNode(_browserContextMenuTargetNode);

        await StartSlideshowFromTreeRootAsync(root, "No folder to start from.").ConfigureAwait(true);
    }

    private void BrowserContextOpenArchiveWizardInFolder_Click()
    {
        string? folder;
        if (_browserContextMenuIsToolbarCurrentFolder)
        {
            _browserContextMenuIsToolbarCurrentFolder = false;
            folder = _currentFolderPath;
        }
        else
        {
            folder = ResolveFolderPathForFavoriteFromNode(_browserContextMenuTargetNode);
        }

        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return;

        ShowOrActivateDeleteArchiveWizardForFolder(folder);
    }

    private static string? ResolveFolderPathForFavoriteFromNode(TreeViewNode? node) =>
        node?.Content switch
        {
            FolderTreeEntry fe => fe.Path,
            ImageRow row => Path.GetDirectoryName(row.FullPath),
            _ => null,
        };

    internal async void QueueExecuteBrowserTreeDeleteFromKeyboardAsync()
    {
        try
        {
            await ExecuteBrowserTreeDeleteForCurrentSelectionAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetTransientStatus("Delete failed: " + ex.Message);
        }
    }

    internal async void BrowserContextDelete_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_browserContextMenuIsToolbarCurrentFolder)
            {
                _browserContextMenuIsToolbarCurrentFolder = false;
                await ExecuteBrowserTreeDeleteToolbarCurrentFolderAsync().ConfigureAwait(true);
                return;
            }

            await ExecuteBrowserTreeDeleteForCurrentSelectionAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetTransientStatus("Delete failed: " + ex.Message);
        }
    }

    private async Task ExecuteBrowserTreeDeleteToolbarCurrentFolderAsync()
    {
        var work = _currentFolderPath;
        if (string.IsNullOrEmpty(work) || !Directory.Exists(work))
            return;
        if (!TreeDeletePathsAreUnderBrowseRoot(new[] { work }))
        {
            SetTransientStatus("Folder is outside the browse tree.");
            return;
        }

        if (!await TryConfirmSendFolderPathToRecycleBinDialogAsync(work).ConfigureAwait(true))
            return;
        await ExecuteSendFolderToRecycleBinAfterConfirmAsync(work).ConfigureAwait(true);
    }

    private bool TreeDeletePathsAreUnderBrowseRoot(IReadOnlyList<string> paths)
    {
        var root = _currentFolderPath;
        if (string.IsNullOrEmpty(root))
            return false;
        foreach (var p in paths)
        {
            if (string.IsNullOrEmpty(p))
                return false;
            if (!IsSameOrDescendantDirectory(root, p))
                return false;
        }

        return true;
    }

    private async Task ExecuteBrowserTreeDeleteForCurrentSelectionAsync()
    {
        if (IsBrowserPaneMutationInProgress)
            return;

        var browseRoot = _currentFolderPath;
        if (string.IsNullOrEmpty(browseRoot))
        {
            SetTransientStatus("Nothing to delete.");
            return;
        }

        var nodes = GetSelectedBrowserTreeNavNodes();
        if (nodes.Count == 0)
        {
            SetTransientStatus("Nothing to delete.");
            return;
        }

        var filePaths = new List<string>();
        var folderPaths = new List<string>();
        foreach (var n in nodes)
        {
            switch (n.Content)
            {
                case ImageRow row:
                    filePaths.Add(row.FullPath);
                    break;
                case FolderTreeEntry fe:
                    folderPaths.Add(fe.Path);
                    break;
            }
        }

        var allProbe = filePaths.Concat(folderPaths).ToList();
        if (!TreeDeletePathsAreUnderBrowseRoot(allProbe))
        {
            SetTransientStatus("Selection is outside the browse folder.");
            return;
        }

        var (files, folders) = BrowserTreeDeletePathDedupe.BuildDeletionPathLists(filePaths, folderPaths);
        var total = files.Count + folders.Count;
        if (total == 0)
            return;

        if (total == 1 && files.Count == 1)
        {
            var fp = files[0];
            if (!File.Exists(fp))
            {
                SetTransientStatus("File no longer exists.");
                return;
            }

            var parent = Path.GetDirectoryName(fp) ?? string.Empty;
            if (WizardImageDeletionPreflight.SuggestsPermanentMayBeNeeded(parent))
            {
                var preDlg = new ContentDialog
                {
                    Title = "Permanent delete may be required",
                    Content = new TextBlock
                    {
                        Text =
                            "This folder appears to be on a network location where the Recycle Bin is often unavailable. " +
                            "If sending to the Recycle Bin fails, ImageHoard can permanently delete those files instead. " +
                            "Permanent deletion cannot be undone from the app.\n\n" +
                            "Files in this operation: 1.",
                        TextWrapping = TextWrapping.WrapWholeWords,
                    },
                    PrimaryButtonText = "Continue",
                    CloseButtonText = "Cancel",
                    XamlRoot = RootGrid.XamlRoot,
                };
                if (await ShowWizardContentDialogAsync(preDlg).ConfigureAwait(true) != ContentDialogResult.Primary)
                    return;
            }

            var dlg = new ContentDialog
            {
                Title = "Delete file?",
                Content = new TextBlock
                {
                    Text = $"Send this file to the Recycle Bin?\n\n{Path.GetFileName(fp)}",
                    TextWrapping = TextWrapping.WrapWholeWords,
                },
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot,
            };
            if (await ShowWizardContentDialogAsync(dlg).ConfigureAwait(true) != ContentDialogResult.Primary)
                return;

            if (!await WizardExecuteImageRecycleOrPermanentBatchAsync(
                    new[] { fp },
                    recordUndoForRecycledPaths: false,
                    "BrowserTreeDelete",
                    browseRoot).ConfigureAwait(true))
                return;
        }
        else if (total == 1 && folders.Count == 1)
        {
            var fd = folders[0];
            if (!Directory.Exists(fd))
            {
                SetTransientStatus("Folder no longer exists.");
                return;
            }

            if (!await TryConfirmSendFolderPathToRecycleBinDialogAsync(fd).ConfigureAwait(true))
                return;
            if (!await ExecuteSendFolderToRecycleBinAfterConfirmAsync(fd).ConfigureAwait(true))
                return;
        }
        else
        {
            var summary = new TextBlock
            {
                Text =
                    $"Send {total} items to the Recycle Bin?\n\n" +
                    $"{files.Count} file(s), {folders.Count} folder(s). " +
                    "Folders include all files and subfolders.",
                TextWrapping = TextWrapping.WrapWholeWords,
            };
            var batchDlg = new ContentDialog
            {
                Title = "Delete items?",
                Content = summary,
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot,
            };
            if (await ShowWizardContentDialogAsync(batchDlg).ConfigureAwait(true) != ContentDialogResult.Primary)
                return;

            if (WizardImageDeletionPreflight.SuggestsPermanentMayBeNeeded(browseRoot) && files.Count > 0)
            {
                var preDlg = new ContentDialog
                {
                    Title = "Permanent delete may be required",
                    Content = new TextBlock
                    {
                        Text =
                            "This folder appears to be on a network location where the Recycle Bin is often unavailable. " +
                            "If sending to the Recycle Bin fails, ImageHoard can permanently delete those files instead. " +
                            "Permanent deletion cannot be undone from the app.\n\n" +
                            $"Files in this operation: {files.Count}.",
                        TextWrapping = TextWrapping.WrapWholeWords,
                    },
                    PrimaryButtonText = "Continue",
                    CloseButtonText = "Cancel",
                    XamlRoot = RootGrid.XamlRoot,
                };
                if (await ShowWizardContentDialogAsync(preDlg).ConfigureAwait(true) != ContentDialogResult.Primary)
                    return;
            }

            if (files.Count > 0)
            {
                if (!await WizardExecuteImageRecycleOrPermanentBatchAsync(
                        files,
                        recordUndoForRecycledPaths: false,
                        "BrowserTreeDelete",
                        browseRoot).ConfigureAwait(true))
                    return;
            }

            foreach (var fd in folders)
            {
                if (!Directory.Exists(fd))
                    continue;
                if (!await ExecuteSendFolderToRecycleBinAfterConfirmAsync(fd).ConfigureAwait(true))
                    return;
            }
        }
    }

    internal IReadOnlyList<TreeViewNode> GetSelectedBrowserTreeNavNodes()
    {
        if (_browse2SyntheticPrimaryNavNode?.Content is FolderTreeEntry or ImageRow)
            return new[] { _browse2SyntheticPrimaryNavNode };

        var img = _browse2Coordinator?.Images.SelectedImagePath;
        if (!string.IsNullOrEmpty(img) && File.Exists(img))
        {
            var name = Path.GetFileName(img);
            var row = new ImageRow(img, name, 0, DateTimeOffset.MinValue, "—", "—", "·");
            ApplySortFlagPresentationToRow(row, img);
            var wrap = new TreeViewNode { Content = row };
            return new[] { wrap };
        }

        return Array.Empty<TreeViewNode>();
    }

    private void BrowseFindInTree_Click(object sender, RoutedEventArgs e) =>
        ShowBrowserFindOverlay();

    internal void ShowBrowserFindOverlay()
    {
        HidePreferencesOverlay();
        HideDeleteArchiveWizardOverlay();

        if (string.IsNullOrEmpty(_currentFolderPath))
            BrowserFindPanelElement.SetStatus("Open a folder first.");
        else
            BrowserFindPanelElement.SetStatus(string.Empty);

        if (IsBrowserFindOverlayOpen)
        {
            BrowserFindPanelElement.OnOverlayShown();
            SetBrowserFindPreviewDimVisible(true);
            return;
        }

        BrowserFindOverlayRoot.Visibility = Visibility.Visible;
        BrowserFindPanelElement.OnOverlayShown();
        SetBrowserFindPreviewDimVisible(true);
    }

    internal void HideBrowserFindOverlay()
    {
        if (!IsBrowserFindOverlayOpen)
            return;

        ClearBrowserFindMatchCacheCore();
        BrowserFindOverlayRoot.Visibility = Visibility.Collapsed;
        BrowserFindPanelElement.OnOverlayHidden();
        SetBrowserFindPreviewDimVisible(false);
    }

    internal void InvalidateBrowserFindCachedResults()
    {
        ClearBrowserFindMatchCacheCore();
        BrowserFindPanelElement.SetStatus("Options changed. Use Next, Previous, or Enter to search.");
    }

    private void ClearBrowserFindMatchCacheCore()
    {
        try
        {
            _browserFindSearchCts?.Cancel();
        }
        catch
        {
            // ignored
        }

        _browserFindSearchCts?.Dispose();
        _browserFindSearchCts = null;
        _browserFindMatches.Clear();
        _browserFindCurrentIndex = 0;
        _browserFindMatchesForParameters = null;
    }

    private void SetBrowserFindPreviewDimVisible(bool visible) =>
        BrowserFindPreviewDimLayer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    internal async Task BrowserFindNavigateAsync(
        int delta,
        string query,
        bool matchFromStartOfName,
        bool foldersOnly,
        bool deepSearch)
    {
        if (Browse2CoordinatorOrNull is { } b2)
            await BrowserFindNavigateBrowse2Async(b2, delta, query, matchFromStartOfName, foldersOnly, deepSearch)
                .ConfigureAwait(true);
    }

    private async Task BrowserFindNavigateBrowse2Async(
        CrossPaneCoordinator coordinator,
        int delta,
        string query,
        bool matchFromStartOfName,
        bool foldersOnly,
        bool deepSearch)
    {
        var sig = new BrowserFindSearchParameters(query.Trim(), matchFromStartOfName, foldersOnly, deepSearch);
        if (_browserFindMatches.Count > 0
            && (!_browserFindMatchesForParameters.HasValue || _browserFindMatchesForParameters.Value != sig))
        {
            ClearBrowserFindMatchCacheCore();
        }

        if (_browserFindMatches.Count == 0)
        {
            var anchor = delta > 0 ? BrowserFindSearchAnchor.First : BrowserFindSearchAnchor.Last;
            await RunBrowserFindSearchFromPanelBrowse2Async(coordinator, query, matchFromStartOfName, foldersOnly, deepSearch, anchor)
                .ConfigureAwait(true);
            return;
        }

        await BrowserFindStepMatchBrowse2Async(coordinator, delta).ConfigureAwait(true);
    }

    internal async Task BrowserFindStepMatchAsync(int delta)
    {
        if (Browse2CoordinatorOrNull is { } b2)
            await BrowserFindStepMatchBrowse2Async(b2, delta).ConfigureAwait(true);
    }

    private async Task BrowserFindStepMatchBrowse2Async(CrossPaneCoordinator coordinator, int delta)
    {
        var n = _browserFindMatches.Count;
        if (n == 0)
            return;

        _browserFindCurrentIndex = ((_browserFindCurrentIndex + delta) % n + n) % n;
        var cur = _browserFindMatches[_browserFindCurrentIndex];
        coordinator.Find.ApplyFindHit(cur);
        await ScheduleViewportAsync(
                BrowserTreeViewportIntentResolver.ForFindHit(
                    BuildBrowserPaneState(),
                    cur))
            .ConfigureAwait(true);
        SetLastActedFsObject(cur.Path);
        BrowserFindPanelElement.SetStatus($"{_browserFindCurrentIndex + 1} of {n}");
    }

    internal async Task RunBrowserFindSearchFromPanelAsync(
        string query,
        bool matchFromStartOfName,
        bool foldersOnly,
        bool deepSearch,
        BrowserFindSearchAnchor anchor = BrowserFindSearchAnchor.First)
    {
        if (Browse2CoordinatorOrNull is { } b2)
            await RunBrowserFindSearchFromPanelBrowse2Async(b2, query, matchFromStartOfName, foldersOnly, deepSearch, anchor)
                .ConfigureAwait(true);
    }

    private async Task RunBrowserFindSearchFromPanelBrowse2Async(
        CrossPaneCoordinator coordinator,
        string query,
        bool matchFromStartOfName,
        bool foldersOnly,
        bool deepSearch,
        BrowserFindSearchAnchor anchor)
    {
        var trimmed = query.Trim();
        if (string.IsNullOrEmpty(_currentFolderPath))
        {
            BrowserFindPanelElement.SetStatus("Open a folder first.");
            return;
        }

        if (string.IsNullOrEmpty(trimmed))
        {
            ClearBrowserFindMatchCacheCore();
            BrowserFindPanelElement.SetStatus("Enter text to search");
            return;
        }

        _browserFindSearchCts?.Cancel();
        _browserFindSearchCts?.Dispose();
        _browserFindSearchCts = new CancellationTokenSource();
        var ct = _browserFindSearchCts.Token;

        BrowserFindPanelElement.SetStatus("Searching…");

        try
        {
            var found = await coordinator.Find
                .SearchAsync(trimmed, matchFromStartOfName, foldersOnly, deepSearch, ct)
                .ConfigureAwait(true);

            if (ct.IsCancellationRequested)
                return;

            _browserFindMatches.Clear();
            _browserFindMatchesForParameters = null;
            _browserFindMatches.AddRange(found);
            if (_browserFindMatches.Count == 0)
            {
                BrowserFindPanelElement.SetStatus("No matches.");
                return;
            }

            _browserFindMatchesForParameters = new BrowserFindSearchParameters(
                trimmed,
                matchFromStartOfName,
                foldersOnly,
                deepSearch);

            _browserFindCurrentIndex = anchor == BrowserFindSearchAnchor.Last
                ? _browserFindMatches.Count - 1
                : 0;
            var cur = _browserFindMatches[_browserFindCurrentIndex];
            coordinator.Find.ApplyFindHit(cur);
            await ScheduleViewportAsync(
                    BrowserTreeViewportIntentResolver.ForFindHit(BuildBrowserPaneState(), cur))
                .ConfigureAwait(true);
            SetLastActedFsObject(cur.Path);
            BrowserFindPanelElement.SetStatus(
                $"{_browserFindCurrentIndex + 1} of {_browserFindMatches.Count}");
        }
        catch (OperationCanceledException)
        {
            BrowserFindPanelElement.SetStatus("Search cancelled.");
        }
        catch (Exception ex)
        {
            BrowserFindPanelElement.SetStatus("Search failed: " + ex.Message);
        }
    }

    private void BrowserFindOverlayRoot_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            HideBrowserFindOverlay();
            e.Handled = true;
            return;
        }

        if (e.Key is not (VirtualKey.Left or VirtualKey.Right))
            return;

        var xamlRoot = BrowserFindOverlayRoot.XamlRoot;
        if (xamlRoot == null)
            return;

        var focused = FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;
        if (BrowserFindPanelElement.IsFocusInsideQueryTextBox())
            return;

        if (IsInsideTextInput(focused))
            return;

        var p = BrowserFindPanelElement.GetBrowserFindSearchParameters();
        if (e.Key == VirtualKey.Right)
            _ = BrowserFindNavigateAsync(1, p.Query, p.MatchFromStartOfName, p.FoldersOnly, p.DeepSearch);
        else
            _ = BrowserFindNavigateAsync(-1, p.Query, p.MatchFromStartOfName, p.FoldersOnly, p.DeepSearch);
        e.Handled = true;
    }

    private void BrowserFindOverlayRoot_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src)
            return;
        if (IsDescendantOf(src, BrowserFindDialogHost))
            return;
        HideBrowserFindOverlay();
        e.Handled = true;
    }

    private async Task ApplyOverlayListPositionFromTreeAsync()
    {
        void hideBoth()
        {
            NormalPathPositionText.Visibility = Visibility.Collapsed;
            FullscreenPathPositionText.Visibility = Visibility.Collapsed;
        }

        if (!_layoutState.ShowOverlayListPosition)
        {
            hideBoth();
            return;
        }

        var path = _currentImageFullPath;
        if (string.IsNullOrEmpty(path))
        {
            hideBoth();
            return;
        }

        if (_slideshowUiActive && _slideshow != null)
        {
            if (!_slideshow.TryGetSlideshowOverlayListPosition(out var index1Based, out var total, out _, out var discovered)
                || index1Based <= 0
                || total <= 0)
            {
                hideBoth();
                return;
            }

            var slideshowText = $"{index1Based}/{total} · discovered {discovered:N0}";
            NormalPathPositionText.Text = slideshowText;
            FullscreenPathPositionText.Text = slideshowText;
            NormalPathPositionText.Visibility = Visibility.Visible;
            FullscreenPathPositionText.Visibility = Visibility.Visible;
            return;
        }

        if (_browse2Coordinator != null
            && _browse2Coordinator.TryGetSlideshowOverlayStyleBrowseFlatLinePosition(
                path,
                out var idx,
                out var tot,
                out var disc))
        {
            var t = $"{idx}/{tot} · discovered {disc:N0}";
            NormalPathPositionText.Text = t;
            FullscreenPathPositionText.Text = t;
            NormalPathPositionText.Visibility = Visibility.Visible;
            FullscreenPathPositionText.Visibility = Visibility.Visible;
            return;
        }

        var sel = TryGetBrowserTreePrimaryNavNode();
        var selPath = sel?.Content is ImageRow sr ? sr.FullPath : null;
        var contextDir = BrowseContextDirectory.Resolve(
            _browseNavAnchorPath,
            selPath,
            path,
            _currentFolderPath,
            TryGetBrowseTreeSelectedFolderPath());
        if (string.IsNullOrEmpty(contextDir))
        {
            hideBoth();
            return;
        }

        var paths = await GetBrowseNavigationPathsForContextAsync(contextDir).ConfigureAwait(true);

        if (paths.Count == 0
            || !string.Equals(_currentImageFullPath, path, StringComparison.OrdinalIgnoreCase))
        {
            hideBoth();
            return;
        }

        var index = paths.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            hideBoth();
            return;
        }

        var text = $"{index + 1}/{paths.Count}";
        NormalPathPositionText.Text = text;
        FullscreenPathPositionText.Text = text;
        NormalPathPositionText.Visibility = Visibility.Visible;
        FullscreenPathPositionText.Visibility = Visibility.Visible;
    }

    private void RefreshSortFlagDisplayInList(string fullPath)
    {
        _ = fullPath;
        if (_browseNavigationMode == BrowseNavigationMode.AllImages)
        {
            SyncBrowse2SyntheticPrimaryNavNode();
            return;
        }

        _browse2Coordinator?.Images.RequestReload();
    }

    private void RefreshAllSortFlagDisplaysInList()
    {
        if (_browseNavigationMode == BrowseNavigationMode.AllImages)
        {
            SyncBrowse2SyntheticPrimaryNavNode();
            return;
        }

        _browse2Coordinator?.Images.RequestReload();
    }
}
