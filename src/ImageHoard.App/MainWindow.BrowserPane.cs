using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageHoard.Core;
using ImageHoard.Core.Browse;
using ImageHoard.Core.Metrics;
using ImageHoard.Core.Models;
using ImageHoard.Core.Services;
using ImageHoard.Core.Sort;
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
    /// <summary>Optional post-wizard browser refocus when a directory was removed and the next sibling should receive focus.</summary>
    internal sealed record BrowserTreeRefocusAfterWizardContext(
        string? PreferredNextFolderFullPath,
        string? ImageDeletionWorkingFolder = null);

    private readonly Dictionary<string, FolderTreeEntry> _folderTreeEntryByPath = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Keys match <see cref="_folderTreeEntryByPath"/> (folder full path at registration time).</summary>
    private readonly Dictionary<string, TreeViewNode> _folderTreeNodeByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long?> _folderAggregateBytesByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int?> _folderImageFileCountByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _folderMetricsInFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _folderMetricsConcurrency = new(2, 2);
    private CancellationTokenSource? _folderMetricsCts = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _folderResortCoalesceTimer;
    /// <summary>Deferred aggregate-sort resorts coalesced by sibling WinRT list identity (same parent <see cref="TreeViewNode.Children"/> or <see cref="TreeView.RootNodes"/>).</summary>
    private readonly HashSet<IList<TreeViewNode>> _folderResortCoalescePendingSiblingLists =
        new(ResortSiblingListReferenceEqualityComparer.Instance);
    private DateTimeOffset? _folderResortCoalesceWindowStartUtc;
    private CancellationTokenSource _browseExpandProbeCts = new();
    private readonly object _pendingFolderMetricsSnapLock = new();
    private readonly List<(string Path, FolderMetricsSnapshot Snap, int Gen)> _pendingFolderMetricsSnapshots = new();
    private bool _folderMetricsUiFlushPending;

    private const int BrowserStagedPopulateFolderThreshold = 120;

    /// <summary>Retries while staged folder populate may not have inserted all sibling <see cref="TreeViewNode"/> rows yet.</summary>
    private const int SiblingFolderNavMaterializeMaxAttempts = 64;
    /// <summary>Folder metrics snapshots merged per inner batch. Larger values reduce dispatcher re-queue churn; tune with Speedscope (Tier C).</summary>
    private const int BrowserMetricsSnapshotApplyChunkSize = 14;
    /// <summary>How many snapshot batches to drain in one <see cref="ProcessPendingFolderMetricsSnapshotsBatched"/> callback before re-queuing.</summary>
    private const int BrowserMetricsSnapshotApplyMaxChunksPerDispatcherCallback = 2;
    private const int BrowserFolderUiChunkSize = 72;
    private const int BrowserMetricsDiscoveryDrainPerTick = 14;
    private const int BrowserMetricsDiscoveryDrainPerTickWhileSettling = 5;
    private const double BrowsePopulateMetricsSettlingMs = 520;

    /// <summary>Updated when browse generation increments; used to reduce metrics discovery drain briefly after navigate.</summary>
    private DateTimeOffset _lastBrowsePopulateUtc = DateTimeOffset.MinValue;

    private readonly ConcurrentQueue<(string Path, FolderMetricsScanScope Scope)> _folderMetricsDiscoveryQueue = new();
    private bool _folderMetricsDrainUiPending;
    private readonly ConcurrentDictionary<TreeViewNode, byte> _folderChildLoadInFlight = new();

    /// <summary>Incremented when the user changes folder sort mode; stale coalesced aggregate resorts must not run afterward.</summary>
    private int _folderResortCancelToken;
    private int _folderResortScheduledToken;

    internal readonly record struct WizardPredeletedFileStat(string FullPath, long LengthBytes, bool IsImage);

    internal readonly record struct WizardRestoredFileStat(string FullPath, long LengthBytes, bool IsImage);

    private TreeViewNode? _renameTargetNode;
    private bool _renameCommitInProgress;

    private readonly object _browserTreeViewportCoalesceLock = new();
    private bool _browserTreeViewportCoalesceScheduled;
    private string? _browserTreeViewportCoalescedPinFolderPath;
    private bool _browserTreeViewportCoalescedPreferSelectionBeforeBrowsed;
    private readonly List<TaskCompletionSource> _browserTreeViewportCoalesceWaiters = new();

    private TreeViewNode? _browserContextMenuTargetNode;
    private MenuFlyout? _browserTreeContextMenu;
    private bool _browserContextMenuIsToolbarCurrentFolder;
    private MenuFlyoutItem? _browserTreeContextMenuRenameItem;

    private readonly BrowserFolderListHeaderMarker _pinnedBrowserFolderListHeaderMarker = new();
    private readonly BrowserFileListHeaderMarker _pinnedBrowserFileListHeaderMarker = new();
    private TreeViewNode? _pinnedBrowserFolderHeaderWrapperNode;
    private TreeViewNode? _pinnedBrowserFileHeaderWrapperNode;

    private void WireBrowserTreeTemplates()
    {
        FolderTree.ItemTemplateSelector = new BrowserTreeItemTemplateSelector
        {
            FolderTemplate = (DataTemplate)RootGrid.Resources["BrowserFolderItemTemplate"],
            FileTemplate = (DataTemplate)RootGrid.Resources["BrowserFileItemTemplate"],
        };
        _pinnedBrowserFolderHeaderWrapperNode = new TreeViewNode
        {
            Content = _pinnedBrowserFolderListHeaderMarker,
            HasUnrealizedChildren = false,
        };
        _pinnedBrowserFileHeaderWrapperNode = new TreeViewNode
        {
            Content = _pinnedBrowserFileListHeaderMarker,
            HasUnrealizedChildren = false,
        };
        BrowserPinnedFolderColumnHeader.Content = _pinnedBrowserFolderHeaderWrapperNode;
        BrowserPinnedFileColumnHeader.Content = _pinnedBrowserFileHeaderWrapperNode;
        SyncPinnedBrowserColumnHeaders();

        FolderTree.DoubleTapped += FolderTree_DoubleTapped;
        FolderTree.PreviewKeyDown += FolderTree_PreviewKeyDown;

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
        var favoriteItem = new MenuFlyoutItem { Text = "Add folder to favorites" };
        favoriteItem.Click += (_, _) => BrowserContextAddFavorite_Click();
        _browserTreeContextMenu.Items.Add(favoriteItem);
    }

    private void BrowserTreeRow_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var item = FindAncestorTreeViewItem(sender as DependencyObject);
        var node = FindNodeForTreeViewItem(item);
        if (node?.Content is not FolderTreeEntry && node?.Content is not ImageRow)
            return;

        e.Handled = true;
        _browserContextMenuIsToolbarCurrentFolder = false;
        _browserContextMenuTargetNode = node;
        if (sender is not FrameworkElement anchor || _browserTreeContextMenu == null)
            return;

        var options = new FlyoutShowOptions { Position = e.GetPosition(anchor) };
        _browserTreeContextMenu.ShowAt(anchor, options);
    }

    private void BrowserBrowseToolbar_RightTapped(object sender, RightTappedRoutedEventArgs e)
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
    }

    private async void BrowserContextRefresh_Click()
    {
        if (_browserContextMenuIsToolbarCurrentFolder)
        {
            _browserContextMenuIsToolbarCurrentFolder = false;
            await RefreshBrowserTreeSelectionScopeAsync(subtreeRoot: null).ConfigureAwait(true);
            return;
        }

        if (_browserContextMenuTargetNode == null)
            return;
        SyncBrowseTreeSelection(_browserContextMenuTargetNode);

        TreeViewNode? subtreeRoot = _browserContextMenuTargetNode.Content switch
        {
            FolderTreeEntry => _browserContextMenuTargetNode,
            ImageRow => _browserContextMenuTargetNode.Parent,
            _ => null,
        };

        if (_browserContextMenuTargetNode.Content is ImageRow && subtreeRoot == null)
        {
            if (!string.IsNullOrEmpty(_currentFolderPath) && Directory.Exists(_currentFolderPath))
                await SynchronizeFolderImageRowsWithDiskAsync(
                        _currentFolderPath,
                        FolderTree.RootNodes,
                        updateTransientStatus: false,
                        refreshExistingFileMetadata: true)
                    .ConfigureAwait(true);
            foreach (var n in EnumerateVisibleFolderTreeNodesPreorder(FolderTree.RootNodes))
            {
                if (n.Content is not FolderTreeEntry fe)
                    continue;
                var scope = n.IsExpanded ? FolderMetricsScanScope.FullSubtree : FolderMetricsScanScope.ImmediateChildren;
                EnqueueFolderMetricsRescan(fe.Path, scope);
            }

            return;
        }

        if (subtreeRoot != null)
            await RefreshBrowserTreeSelectionScopeAsync(subtreeRoot).ConfigureAwait(true);
    }

    /// <summary>
    /// Resyncs image rows and re-queues folder metrics for <paramref name="subtreeRoot"/> and its loaded descendants;
    /// pass <c>null</c> to refresh the whole browse tree (toolbar path row).
    /// </summary>
    private async Task RefreshBrowserTreeSelectionScopeAsync(TreeViewNode? subtreeRoot)
    {
        if (subtreeRoot == null)
        {
            if (!string.IsNullOrEmpty(_currentFolderPath) && Directory.Exists(_currentFolderPath))
                await SynchronizeFolderImageRowsWithDiskAsync(
                        _currentFolderPath,
                        FolderTree.RootNodes,
                        updateTransientStatus: true,
                        refreshExistingFileMetadata: true)
                    .ConfigureAwait(true);

            foreach (var n in EnumerateNodesDepthFirst(FolderTree.RootNodes))
            {
                if (n.Content is not FolderTreeEntry)
                    continue;
                if (!n.IsExpanded || n.HasUnrealizedChildren)
                    continue;
                await SynchronizeFolderImageRowsWithDiskAsync(
                        ((FolderTreeEntry)n.Content).Path,
                        n.Children,
                        updateTransientStatus: false,
                        refreshExistingFileMetadata: true).ConfigureAwait(true);
            }

            foreach (var n in EnumerateVisibleFolderTreeNodesPreorder(FolderTree.RootNodes))
            {
                if (n.Content is not FolderTreeEntry fe)
                    continue;
                var scope = n.IsExpanded ? FolderMetricsScanScope.FullSubtree : FolderMetricsScanScope.ImmediateChildren;
                EnqueueFolderMetricsRescan(fe.Path, scope);
            }

            return;
        }

        foreach (var n in EnumerateNodesDepthFirst(subtreeRoot))
        {
            if (n.Content is not FolderTreeEntry)
                continue;
            if (!n.IsExpanded || n.HasUnrealizedChildren)
                continue;
            await SynchronizeFolderImageRowsWithDiskAsync(
                    ((FolderTreeEntry)n.Content).Path,
                    n.Children,
                    updateTransientStatus: false,
                    refreshExistingFileMetadata: true).ConfigureAwait(true);
        }

        foreach (var n in EnumerateNodesDepthFirst(subtreeRoot))
        {
            if (n.Content is not FolderTreeEntry fe)
                continue;
            var scope = n.IsExpanded ? FolderMetricsScanScope.FullSubtree : FolderMetricsScanScope.ImmediateChildren;
            EnqueueFolderMetricsRescan(fe.Path, scope);
        }
    }

    private void BrowserContextRename_Click()
    {
        if (_browserContextMenuIsToolbarCurrentFolder)
        {
            _browserContextMenuIsToolbarCurrentFolder = false;
            BrowserContextRenameCurrentBrowseFolderAsync();
            return;
        }

        if (_browserContextMenuTargetNode == null)
            return;
        SyncBrowseTreeSelection(_browserContextMenuTargetNode);
        TryBeginRenameSelectedBrowserItem();
    }

    private async void BrowserContextRenameCurrentBrowseFolderAsync()
    {
        var oldPath = _currentFolderPath;
        if (string.IsNullOrEmpty(oldPath) || !Directory.Exists(oldPath))
            return;

        var name = Path.GetFileName(oldPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
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

        var synthetic = new FolderTreeEntry(oldPath, name);
        await CommitFolderRenameAsync(synthetic, box.Text).ConfigureAwait(true);
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

    private static string? ResolveFolderPathForFavoriteFromNode(TreeViewNode? node) =>
        node?.Content switch
        {
            FolderTreeEntry fe => fe.Path,
            ImageRow row => Path.GetDirectoryName(row.FullPath),
            _ => null,
        };

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

    private void SyncPinnedBrowserColumnHeaders()
    {
        if (_pinnedBrowserFolderHeaderWrapperNode == null || _pinnedBrowserFileHeaderWrapperNode == null)
            return;

        ApplyLayoutFolderDetailsToFolderHeaderMarker(_pinnedBrowserFolderListHeaderMarker);
        ApplyLayoutFileDetailsToHeaderMarker(_pinnedBrowserFileListHeaderMarker);

        var roots = FolderTree.RootNodes;
        var showFolder = _layoutState.ShowBrowserFolderColumnHeadings
                         && AnyRootNodeContentIs<FolderTreeEntry>(roots);
        var showFile = _layoutState.ShowBrowserFileColumnHeadings
                       && AnyRootNodeContentIs<ImageRow>(roots);

        BrowserPinnedFolderColumnHeader.Visibility = showFolder ? Visibility.Visible : Visibility.Collapsed;
        BrowserPinnedFileColumnHeader.Visibility = showFile ? Visibility.Visible : Visibility.Collapsed;
        BrowserPinnedColumnHeadersHost.Visibility = showFolder || showFile ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool AnyRootNodeContentIs<T>(IList<TreeViewNode> roots) where T : class
    {
        for (var i = 0; i < roots.Count; i++)
        {
            if (roots[i].Content is T)
                return true;
        }

        return false;
    }

    private void PrepareFolderEntrySizingState(FolderTreeEntry entry)
    {
        if (!_layoutState.ShowBrowserFolderSize)
            entry.ClearAggregateSizeUnavailable();
        else if (_layoutState.CalculateFolderSizesInBackground)
            entry.ClearAggregateSizePending();
        else
            entry.ClearAggregateSizeUnavailable();

        if (!_layoutState.ShowBrowserFolderImageCount)
            entry.ClearImageCountUnavailable();
        else if (_layoutState.CalculateFolderSizesInBackground)
            entry.ClearImageCountPending();
        else
            entry.ClearImageCountUnavailable();
    }

    private void RegisterFolderTreeIndex(FolderTreeEntry entry, TreeViewNode? hostNode = null)
    {
        _folderTreeEntryByPath[entry.Path] = entry;
        if (hostNode != null)
            _folderTreeNodeByPath[entry.Path] = hostNode;
        var agg = entry.AggregateSizeBytes;
        if (agg != null)
            _folderAggregateBytesByPath[entry.Path] = agg;
        var img = entry.ImageFileCount;
        if (img != null)
            _folderImageFileCountByPath[entry.Path] = img;
    }

    private void UnregisterFolderTreeNodeIndex(string folderPath) =>
        _folderTreeNodeByPath.Remove(folderPath);

    private void ResetBrowserFolderMetricsState()
    {
        _folderTreeEntryByPath.Clear();
        _folderTreeNodeByPath.Clear();
        _folderAggregateBytesByPath.Clear();
        _folderImageFileCountByPath.Clear();
        _folderMetricsInFlight.Clear();
        _folderMetricsCts?.Cancel();
        _folderMetricsCts?.Dispose();
        _folderMetricsCts = new CancellationTokenSource();
        lock (_pendingFolderMetricsSnapLock)
        {
            _pendingFolderMetricsSnapshots.Clear();
            _folderMetricsUiFlushPending = false;
        }

        _browseExpandProbeCts.Cancel();
        _browseExpandProbeCts.Dispose();
        _browseExpandProbeCts = new CancellationTokenSource();

        while (_folderMetricsDiscoveryQueue.TryDequeue(out _))
        {
        }

        _folderMetricsDrainUiPending = false;
    }

    internal void CancelFolderResortCoalesceState()
    {
        Interlocked.Increment(ref _folderResortCancelToken);
        StopFolderResortCoalesceTimer();
        _folderResortCoalescePendingSiblingLists.Clear();
        _folderResortCoalesceWindowStartUtc = null;
    }

    private Task RunOnUiAsync(Func<Task> work)
    {
        var dq = DispatcherQueue;
        if (dq.HasThreadAccess)
            return work();

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dq.TryEnqueue(() =>
            {
                _ = InvokeOnUiAsync();

                async Task InvokeOnUiAsync()
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
                }
            }))
        {
            tcs.TrySetResult();
        }

        return tcs.Task;
    }

    private Task RunOnUiSync(Action action) =>
        RunOnUiAsync(() =>
        {
            action();
            return Task.CompletedTask;
        });

    private void ScheduleProcessFolderMetricsDiscoveryQueue()
    {
        if (_folderMetricsDrainUiPending)
            return;
        _folderMetricsDrainUiPending = true;
        _ = DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            ProcessFolderMetricsDiscoveryQueueTick);
    }

    private int BrowserMetricsDiscoveryDrainPerTickForCurrentLoad()
    {
        if ((DateTimeOffset.UtcNow - _lastBrowsePopulateUtc).TotalMilliseconds < BrowsePopulateMetricsSettlingMs)
            return BrowserMetricsDiscoveryDrainPerTickWhileSettling;
        return BrowserMetricsDiscoveryDrainPerTick;
    }

    private void ProcessFolderMetricsDiscoveryQueueTick()
    {
        _folderMetricsDrainUiPending = false;
        var drained = 0;
        var perTick = BrowserMetricsDiscoveryDrainPerTickForCurrentLoad();
        while (drained < perTick
               && _folderMetricsDiscoveryQueue.TryDequeue(out var item))
        {
            drained++;
            _ = StartFolderMetricsWorkAsync(item.Path, item.Scope);
        }

        if (!_folderMetricsDiscoveryQueue.IsEmpty)
            ScheduleProcessFolderMetricsDiscoveryQueue();
    }

    private void EnqueueFolderMetricsDiscovery(string path, FolderMetricsScanScope scope)
    {
        if (!_layoutState.CalculateFolderSizesInBackground
            || (!_layoutState.ShowBrowserFolderSize && !_layoutState.ShowBrowserFolderImageCount))
            return;
        _folderMetricsDiscoveryQueue.Enqueue((path, scope));
        ScheduleProcessFolderMetricsDiscoveryQueue();
    }

    private bool WillQueueImmediateFolderMetricsOnPopulate() =>
        _layoutState.CalculateFolderSizesInBackground
        && (_layoutState.ShowBrowserFolderSize || _layoutState.ShowBrowserFolderImageCount);

    private void AppendBrowserFolderAndImageNodes(
        IList<TreeViewNode> target,
        IEnumerable<FileSystemEntry> dirEntries,
        IReadOnlyList<ImageRow> rows,
        int browserPopulateGeneration,
        bool deferFolderMetricsBulk = false,
        bool directoriesAlreadySorted = false)
    {
        List<FileSystemEntry> dirs = directoriesAlreadySorted
            ? (dirEntries is List<FileSystemEntry> l ? l : dirEntries.ToList())
            : FolderDirectorySort.SortDirectories(
                    dirEntries,
                    _layoutState.FolderListSort,
                    _folderAggregateBytesByPath,
                    _folderImageFileCountByPath)
                .ToList();

        var skipExpandProbeForMetrics = WillQueueImmediateFolderMetricsOnPopulate();
        List<(string Path, TreeViewNode Node)>? expandProbeTargets = !skipExpandProbeForMetrics && dirs.Count > 0
            ? new List<(string Path, TreeViewNode Node)>(dirs.Count)
            : null;

        foreach (var d in dirs)
        {
            var entry = FolderTreeEntry.FromDirectoryEntry(d);
            PrepareFolderEntrySizingState(entry);
            ApplyLayoutFolderDetailsToFolderEntry(entry);
            var node = new TreeViewNode
            {
                Content = entry,
            };
            node.HasUnrealizedChildren = true;
            RegisterFolderTreeIndex(entry, node);
            expandProbeTargets?.Add((d.FullPath, node));
            target.Add(node);
            if (deferFolderMetricsBulk)
                EnqueueFolderMetricsDiscovery(entry.Path, FolderMetricsScanScope.ImmediateChildren);
            else
                EnqueueFolderMetricsScanIfNeeded(entry.Path, FolderMetricsScanScope.ImmediateChildren);
        }

        if (expandProbeTargets is { Count: > 0 })
            ScheduleBrowseExpandabilityProbeBatch(expandProbeTargets, browserPopulateGeneration);

        foreach (var r in rows)
        {
            target.Add(new TreeViewNode
            {
                Content = r,
                HasUnrealizedChildren = false,
            });
        }
    }

    private static bool ShouldStagedBrowserPopulate(int dirCount) =>
        dirCount >= BrowserStagedPopulateFolderThreshold;

    private async Task AppendBrowserStagedToTargetAsync(
        IList<TreeViewNode> target,
        IReadOnlyList<FileSystemEntry> sortedDirs,
        IReadOnlyList<ImageRow> rows,
        int populateGen,
        int browserProbeGen,
        int flatEntryCount,
        bool rootBrowsePopulate)
    {
        var skipExpandProbeForMetrics = WillQueueImmediateFolderMetricsOnPopulate();
        List<(string Path, TreeViewNode Node)>? expandProbeTargets = skipExpandProbeForMetrics
            ? null
            : new List<(string Path, TreeViewNode Node)>(sortedDirs.Count);
        var folderInsertIndex = 0;

        if (populateGen != Volatile.Read(ref _populateBrowserGeneration))
            return;

        foreach (var r in rows)
        {
            target.Add(
                new TreeViewNode
                {
                    Content = r,
                    HasUnrealizedChildren = false,
                });
        }

        SyncPinnedBrowserColumnHeaders();

        foreach (var (offset, length) in ChunkPlanner.EnumerateChunks(sortedDirs.Count, BrowserFolderUiChunkSize))
        {
            if (populateGen != Volatile.Read(ref _populateBrowserGeneration))
                return;

            for (var i = 0; i < length; i++)
            {
                var d = sortedDirs[offset + i];
                var entry = FolderTreeEntry.FromDirectoryEntry(d);
                PrepareFolderEntrySizingState(entry);
                ApplyLayoutFolderDetailsToFolderEntry(entry);
                var treeNode = new TreeViewNode
                {
                    Content = entry,
                };
                treeNode.HasUnrealizedChildren = true;
                RegisterFolderTreeIndex(entry, treeNode);
                expandProbeTargets?.Add((d.FullPath, treeNode));
                target.Insert(folderInsertIndex, treeNode);
                folderInsertIndex++;
                EnqueueFolderMetricsDiscovery(entry.Path, FolderMetricsScanScope.ImmediateChildren);
            }

            await Task.Yield();
        }

        if (rootBrowsePopulate)
            FinalizeBrowserRootPopulateSelectionAndChrome(populateGen, rows);

        if (expandProbeTargets is { Count: > 0 })
            ScheduleBrowseExpandabilityProbeBatch(expandProbeTargets, browserProbeGen);

        SyncPinnedBrowserColumnHeaders();

        if (rootBrowsePopulate)
            FinalizeBrowserRootPopulateStatusAndPersist(populateGen, flatEntryCount, sortedDirs.Count, rows);

        if (rootBrowsePopulate)
            ScheduleBrowserTreeViewportAfterMutation();
    }

    private void FinalizeBrowserRootPopulateSelectionAndChrome(int populateGen, IReadOnlyList<ImageRow> rows)
    {
        if (populateGen != Volatile.Read(ref _populateBrowserGeneration))
            return;

        var selectPath = _pendingSelectImagePath;
        _pendingSelectImagePath = null;

        TreeViewNode? selectNode = null;
        if (!string.IsNullOrEmpty(selectPath))
            selectNode = FindImageNodeByPath(FolderTree.RootNodes, selectPath);
        if (selectNode == null && rows.Count > 0)
            selectNode = FirstImageNodePreorder(FolderTree.RootNodes);

        if (selectNode != null)
            SyncBrowseTreeSelection(selectNode);
        else if (rows.Count == 0)
            ClearImageSelectionAndPreviewCore();

        UpdatePathOverlays();
        UpdateFullscreenMenuEnabled();
        SyncPinnedBrowserColumnHeaders();
    }

    private void FinalizeBrowserRootPopulateStatusAndPersist(int populateGen, int flatEntryCount, int dirCount, IReadOnlyList<ImageRow> rows)
    {
        if (populateGen != Volatile.Read(ref _populateBrowserGeneration))
            return;

        var status = rows.Count == 0 && flatEntryCount > 0
            ? $"0 image(s) · {flatEntryCount} item(s) in folder (none match supported raster extensions)"
            : $"{rows.Count} image(s) · {dirCount} folder(s)";
        SetTransientStatus(status);
        SchedulePersistLayoutDebounced();
    }

    private void FinalizeBrowserRootPopulateAfterImmediateAppend(
        int gen,
        int flatEntryCount,
        int dirCount,
        IReadOnlyList<ImageRow> rows)
    {
        if (gen != Volatile.Read(ref _populateBrowserGeneration))
            return;

        var status = rows.Count == 0 && flatEntryCount > 0
            ? $"0 image(s) · {flatEntryCount} item(s) in folder (none match supported raster extensions)"
            : $"{rows.Count} image(s) · {dirCount} folder(s)";
        SetTransientStatus(status);

        var selectPath = _pendingSelectImagePath;
        _pendingSelectImagePath = null;

        TreeViewNode? selectNode = null;
        if (!string.IsNullOrEmpty(selectPath))
            selectNode = FindImageNodeByPath(FolderTree.RootNodes, selectPath);
        if (selectNode == null && rows.Count > 0)
            selectNode = FirstImageNodePreorder(FolderTree.RootNodes);

        if (selectNode != null)
            SyncBrowseTreeSelection(selectNode);
        else if (rows.Count == 0)
            ClearImageSelectionAndPreviewCore();

        UpdatePathOverlays();
        UpdateFullscreenMenuEnabled();
        SchedulePersistLayoutDebounced();
        SyncPinnedBrowserColumnHeaders();
        ScheduleBrowserTreeViewportAfterMutation();
    }

    private static void CollectBrowserFolderChildListsPreorder(
        IList<TreeViewNode> roots,
        List<IList<TreeViewNode>> lists)
    {
        lists.Clear();
        lists.Add(roots);
        foreach (var n in EnumerateNodesDepthFirst(roots))
        {
            if (n.Children.Count > 0)
                lists.Add(n.Children);
        }
    }

    private void ResortFolderLists(List<IList<TreeViewNode>> lists)
    {
        foreach (var list in lists)
            ResortFolderSiblingBlock(list);
    }

    private void ResortAllFolderGroups()
    {
        var lists = new List<IList<TreeViewNode>>();
        CollectBrowserFolderChildListsPreorder(FolderTree.RootNodes, lists);
        ResortFolderLists(lists);
        SyncPinnedBrowserColumnHeaders();
    }

    private void ApplyBrowserFolderDetailsChrome()
    {
        foreach (var n in EnumerateNodesDepthFirst(FolderTree.RootNodes))
        {
            if (n.Content is FolderTreeEntry row)
                ApplyLayoutFolderDetailsToFolderEntry(row);
        }

        SyncPinnedBrowserColumnHeaders();
    }

    private void ResortFolderSiblingBlock(IList<TreeViewNode> children)
    {
        var folderNodes = new List<TreeViewNode>();
        foreach (var n in children)
        {
            if (n.Content is FolderTreeEntry)
                folderNodes.Add(n);
        }

        if (folderNodes.Count == 0)
            return;

        folderNodes.Sort((x, y) => CompareFolderTreeNodes(x, y, _layoutState.FolderListSort));

        var currentFolderOrder = new List<TreeViewNode>();
        foreach (var n in children)
        {
            if (n.Content is FolderTreeEntry)
                currentFolderOrder.Add(n);
        }

        if (currentFolderOrder.Count == folderNodes.Count)
        {
            var unchanged = true;
            for (var i = 0; i < folderNodes.Count; i++)
            {
                if (!ReferenceEquals(currentFolderOrder[i], folderNodes[i]))
                {
                    unchanged = false;
                    break;
                }
            }

            if (unchanged)
                return;
        }

        var folderSet = new HashSet<TreeViewNode>(folderNodes);
        for (var i = children.Count - 1; i >= 0; i--)
        {
            if (children[i].Content is FolderTreeEntry && folderSet.Contains(children[i]))
                children.RemoveAt(i);
        }

        const int insertAt = 0;
        for (var i = 0; i < folderNodes.Count; i++)
            children.Insert(insertAt + i, folderNodes[i]);
    }

    private static void AddResortSiblingListUnique(List<IList<TreeViewNode>> lists, IList<TreeViewNode> candidate)
    {
        foreach (var x in lists)
        {
            if (ReferenceEquals(x, candidate))
                return;
        }

        lists.Add(candidate);
    }

    /// <summary>
    /// Resolves the WinRT sibling list that contains folder <paramref name="folderFullPath"/> for aggregate resort.
    /// Prefer <see cref="_folderTreeNodeByPath"/> (Tier A); fall back to scanning the visual tree per
    /// docs/design-decisions/browser-folder-tree-path-to-node-index.md.
    /// </summary>
    private bool TryGetResortSiblingListForFolderPath(string folderFullPath, [NotNullWhen(true)] out IList<TreeViewNode>? siblingList)
    {
        siblingList = null;
        if (string.IsNullOrEmpty(folderFullPath))
            return false;

        TreeViewNode? node = _folderTreeNodeByPath.TryGetValue(folderFullPath, out var indexed)
            ? indexed
            : FindFolderTreeNodeByPath(FolderTree.RootNodes, folderFullPath);
        if (node == null)
            return false;

        siblingList = node.Parent != null ? node.Parent.Children : FolderTree.RootNodes;
        return true;
    }

    private void CollectResortSiblingListsForFolderPath(string folderFullPath, List<IList<TreeViewNode>> lists)
    {
        if (!TryGetResortSiblingListForFolderPath(folderFullPath, out var list))
            return;
        AddResortSiblingListUnique(lists, list);
    }

    private void RequestCoalescedFolderResortForTouchedFolderPaths(IEnumerable<string> folderPaths)
    {
        if (!IsDeferredFolderMetricsSort(_layoutState.FolderListSort))
            return;
        foreach (var p in folderPaths)
        {
            if (string.IsNullOrEmpty(p))
                continue;
            if (TryGetResortSiblingListForFolderPath(p, out var list))
                _folderResortCoalescePendingSiblingLists.Add(list);
        }

        if (_folderResortCoalescePendingSiblingLists.Count == 0)
            return;

        _folderResortScheduledToken = Volatile.Read(ref _folderResortCancelToken);

        if (_folderResortCoalesceWindowStartUtc == null)
            _folderResortCoalesceWindowStartUtc = DateTimeOffset.UtcNow;

        var elapsed = DateTimeOffset.UtcNow - _folderResortCoalesceWindowStartUtc.Value;
        if (elapsed >= TimeSpan.FromMilliseconds(520))
        {
            StopFolderResortCoalesceTimer();
            FlushCoalescedFolderResorts();
            return;
        }

        var dq = DispatcherQueue;
        _folderResortCoalesceTimer ??= dq.CreateTimer();
        var remainingToMaxWait = TimeSpan.FromMilliseconds(520) - elapsed;
        var debounce = TimeSpan.FromMilliseconds(420);
        var interval = remainingToMaxWait < debounce ? remainingToMaxWait : debounce;
        if (interval <= TimeSpan.Zero)
            interval = TimeSpan.FromMilliseconds(1);

        _folderResortCoalesceTimer.Interval = interval;
        _folderResortCoalesceTimer.IsRepeating = false;
        _folderResortCoalesceTimer.Tick -= OnFolderResortCoalesceTick;
        _folderResortCoalesceTimer.Tick += OnFolderResortCoalesceTick;
        _folderResortCoalesceTimer.Stop();
        _folderResortCoalesceTimer.Start();
    }

    private void StopFolderResortCoalesceTimer()
    {
        if (_folderResortCoalesceTimer == null)
            return;
        _folderResortCoalesceTimer.Tick -= OnFolderResortCoalesceTick;
        _folderResortCoalesceTimer.Stop();
    }

    /// <summary>
    /// Applies pending deferred sibling-list resorts. Caller must have verified
    /// <see cref="_folderResortCancelToken"/> matches <see cref="_folderResortScheduledToken"/> and that
    /// <see cref="_folderResortCoalescePendingSiblingLists"/> is non-empty.
    /// </summary>
    private void ApplyPendingCoalescedFolderResortSiblingListsCore()
    {
        foreach (var list in _folderResortCoalescePendingSiblingLists)
            ResortFolderSiblingBlock(list);
        _folderResortCoalescePendingSiblingLists.Clear();
        SyncPinnedBrowserColumnHeaders();
    }

    /// <summary>
    /// Before wizard refresh pins the tree viewport: cancel deferred-resort debounce and apply any pending
    /// sibling sorts so <see cref="ScheduleBrowserTreeViewportAfterMutationAsync"/> runs against final order.
    /// </summary>
    private void PrepareBrowserTreeViewportAfterWizardMutation()
    {
        if (!IsDeferredFolderMetricsSort(_layoutState.FolderListSort))
            return;
        StopFolderResortCoalesceTimer();
        if (Volatile.Read(ref _folderResortCancelToken) != _folderResortScheduledToken)
        {
            _folderResortCoalescePendingSiblingLists.Clear();
            _folderResortCoalesceWindowStartUtc = null;
            return;
        }

        if (_folderResortCoalescePendingSiblingLists.Count == 0)
            return;

        _folderResortCoalesceWindowStartUtc = null;
        ApplyPendingCoalescedFolderResortSiblingListsCore();
    }

    private void FlushCoalescedFolderResorts()
    {
        if (Volatile.Read(ref _folderResortCancelToken) != _folderResortScheduledToken)
        {
            _folderResortCoalescePendingSiblingLists.Clear();
            _folderResortCoalesceWindowStartUtc = null;
            return;
        }

        _folderResortCoalesceWindowStartUtc = null;
        if (_folderResortCoalescePendingSiblingLists.Count == 0)
            ResortAllFolderGroups();
        else
            ApplyPendingCoalescedFolderResortSiblingListsCore();
        ScheduleBrowserTreeViewportAfterMutation();
    }

    private void OnFolderResortCoalesceTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        sender.Tick -= OnFolderResortCoalesceTick;
        FlushCoalescedFolderResorts();
    }

    private static bool IsDeferredFolderMetricsSort(FolderListSortKind kind) =>
        kind is FolderListSortKind.AggregateSize or FolderListSortKind.ImageFileCount;

    private static int CompareFolderTreeNodes(TreeViewNode a, TreeViewNode b, FolderListSortKind kind)
    {
        if (a.Content is not FolderTreeEntry fa || b.Content is not FolderTreeEntry fb)
            return 0;
        return CompareFolderEntries(fa, fb, kind);
    }

    private static int CompareFolderEntries(FolderTreeEntry a, FolderTreeEntry b, FolderListSortKind kind)
    {
        return kind switch
        {
            FolderListSortKind.DateModified => CompareDateFolder(a, b),
            FolderListSortKind.AggregateSize => CompareAggFolder(a, b),
            FolderListSortKind.ImageFileCount => CompareImageCountFolder(a, b),
            _ => CompareNameNaturalFolder(a, b),
        };
    }

    private static int CompareNameNaturalFolder(FolderTreeEntry a, FolderTreeEntry b)
    {
        var c = NaturalStringComparer.OrdinalIgnoreCase.Compare(a.DisplayLabel, b.DisplayLabel);
        if (c != 0)
            return c;
        return string.CompareOrdinal(a.Path, b.Path);
    }

    private static int CompareDateFolder(FolderTreeEntry a, FolderTreeEntry b)
    {
        var ta = a.DirectoryLastWriteUtc;
        var tb = b.DirectoryLastWriteUtc;
        if (ta == null && tb == null)
            return CompareNameNaturalFolder(a, b);
        if (ta == null)
            return 1;
        if (tb == null)
            return -1;
        var c = tb.Value.CompareTo(ta.Value);
        if (c != 0)
            return c;
        return string.CompareOrdinal(a.Path, b.Path);
    }

    private static int CompareAggFolder(FolderTreeEntry a, FolderTreeEntry b)
    {
        var sa = a.AggregateSizeBytes;
        var sb = b.AggregateSizeBytes;
        var ha = sa.HasValue ? 0 : 1;
        var hb = sb.HasValue ? 0 : 1;
        var c = ha.CompareTo(hb);
        if (c != 0)
            return c;
        if (ha == 0)
        {
            c = sb!.Value.CompareTo(sa!.Value);
            if (c != 0)
                return c;
        }

        return CompareNameNaturalFolder(a, b);
    }

    private static int CompareImageCountFolder(FolderTreeEntry a, FolderTreeEntry b)
    {
        var ia = a.ImageFileCount;
        var ib = b.ImageFileCount;
        var ha = ia.HasValue ? 0 : 1;
        var hb = ib.HasValue ? 0 : 1;
        var c = ha.CompareTo(hb);
        if (c != 0)
            return c;
        if (ha == 0)
        {
            c = ib!.Value.CompareTo(ia!.Value);
            if (c != 0)
                return c;
        }

        return CompareNameNaturalFolder(a, b);
    }

    private static string FolderMetricsFlightKey(string path, FolderMetricsScanScope scope) =>
        path + "\u001E" + (int)scope;

    private void EnqueueFolderMetricsScanIfNeeded(string path, FolderMetricsScanScope scope)
    {
        if (!_layoutState.CalculateFolderSizesInBackground
            || (!_layoutState.ShowBrowserFolderSize && !_layoutState.ShowBrowserFolderImageCount))
            return;
        _ = StartFolderMetricsWorkAsync(path, scope, ignoreCache: false);
    }

    private void EnqueueFolderMetricsRescan(string path, FolderMetricsScanScope scope)
    {
        if (!_layoutState.CalculateFolderSizesInBackground
            || (!_layoutState.ShowBrowserFolderSize && !_layoutState.ShowBrowserFolderImageCount))
            return;
        var flightKey = FolderMetricsFlightKey(path, scope);
        _folderMetricsInFlight.TryRemove(flightKey, out _);
        _ = StartFolderMetricsWorkAsync(path, scope, ignoreCache: true);
    }

    private async Task StartFolderMetricsWorkAsync(string path, FolderMetricsScanScope scope, bool ignoreCache = false)
    {
        if (!_layoutState.CalculateFolderSizesInBackground
            || (!_layoutState.ShowBrowserFolderSize && !_layoutState.ShowBrowserFolderImageCount))
            return;
        var flightKey = FolderMetricsFlightKey(path, scope);
        if (!_folderMetricsInFlight.TryAdd(flightKey, 0))
            return;

        await _folderMetricsConcurrency.WaitAsync().ConfigureAwait(false);
        try
        {
            var gen = Volatile.Read(ref _populateBrowserGeneration);
            FolderMetricsSnapshot? cached = null;
            try
            {
                cached = await FolderMetricsCacheStore.TryGetLatestSnapshotForPathAsync(
                        AppDataPaths.FolderMetricsCachePath,
                        path,
                        scope,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }

            if (gen != Volatile.Read(ref _populateBrowserGeneration))
                return;

            var dirMtime = TryReadDirectoryMetricsMtimeUtc(path);
            var trustCacheOnly = !ignoreCache
                && cached != null
                && cached.ScanScope == scope
                && FolderMetricsMtimeMatches(cached.FolderMtimeUtc, dirMtime);

            if (trustCacheOnly)
            {
                EnqueueFolderMetricsSnapshotForUiApply(path, cached!, gen);
                return;
            }

            var cts = _folderMetricsCts;
            if (cts == null)
                return;

            FolderMetricsSnapshot snap;
            try
            {
                snap = scope == FolderMetricsScanScope.FullSubtree
                    ? await FolderMetricsScanner.ScanSubtreeAsync(AppServices.FileSystem, path, cts.Token)
                        .ConfigureAwait(false)
                    : await FolderMetricsScanner.ScanImmediateFilesAsync(AppServices.FileSystem, path, cts.Token)
                        .ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            if (gen != Volatile.Read(ref _populateBrowserGeneration))
                return;

            try
            {
                await FolderMetricsCacheStore.AppendSnapshotAsync(AppDataPaths.FolderMetricsCachePath, snap, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }

            EnqueueFolderMetricsSnapshotForUiApply(path, snap, gen);
        }
        finally
        {
            _folderMetricsConcurrency.Release();
            _folderMetricsInFlight.TryRemove(flightKey, out _);
        }
    }

    private static DateTimeOffset? TryReadDirectoryMetricsMtimeUtc(string directoryPath)
    {
        try
        {
            var di = new DirectoryInfo(directoryPath);
            if (!di.Exists)
                return null;
            return new DateTimeOffset(di.LastWriteTimeUtc);
        }
        catch
        {
            return null;
        }
    }

    private static bool FolderMetricsMtimeMatches(DateTimeOffset? cached, DateTimeOffset? onDisk) =>
        cached.HasValue == onDisk.HasValue && (!cached.HasValue || cached.Value.Equals(onDisk!.Value));

    private void ApplySuccessfulWizardDeletesToIndexedFolderAggregates(IReadOnlyList<WizardPredeletedFileStat> succeeded)
    {
        foreach (var s in succeeded)
        {
            var dir = Path.GetDirectoryName(s.FullPath);
            while (!string.IsNullOrEmpty(dir))
            {
                if (_folderTreeEntryByPath.TryGetValue(dir, out var fe))
                {
                    if (fe.AggregateSizeBytes is long bytes)
                        fe.SetAggregateSize(Math.Max(0, bytes - s.LengthBytes));
                    _folderAggregateBytesByPath[dir] = fe.AggregateSizeBytes;

                    if (s.IsImage && fe.ImageFileCount is int ic)
                        fe.SetImageFileCount(Math.Max(0, ic - 1));
                    _folderImageFileCountByPath[dir] = fe.ImageFileCount;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }
        }
    }

    private static void CollectIndexedAncestorPathsForResort(string fileOrFolderPath, HashSet<string> resortPaths)
    {
        var dir = Path.GetDirectoryName(fileOrFolderPath);
        while (!string.IsNullOrEmpty(dir))
        {
            resortPaths.Add(dir);
            dir = Directory.GetParent(dir)?.FullName;
        }
    }

    private void ApplyRestoredWizardFilesToIndexedFolderAggregates(IReadOnlyList<WizardRestoredFileStat> restored)
    {
        foreach (var s in restored)
        {
            var dir = Path.GetDirectoryName(s.FullPath);
            while (!string.IsNullOrEmpty(dir))
            {
                if (_folderTreeEntryByPath.TryGetValue(dir, out var fe))
                {
                    if (fe.AggregateSizeBytes is long bytes)
                        fe.SetAggregateSize(bytes + s.LengthBytes);
                    else
                        fe.SetAggregateSize(s.LengthBytes);

                    _folderAggregateBytesByPath[dir] = fe.AggregateSizeBytes;

                    if (s.IsImage)
                    {
                        if (fe.ImageFileCount is int ic)
                            fe.SetImageFileCount(ic + 1);
                        else
                            fe.SetImageFileCount(1);
                    }

                    _folderImageFileCountByPath[dir] = fe.ImageFileCount;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }
        }
    }

    private static bool TryFindDescendantScrollViewer(DependencyObject root, out ScrollViewer? scrollViewer)
    {
        scrollViewer = null;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv)
            {
                scrollViewer = sv;
                return true;
            }

            if (TryFindDescendantScrollViewer(child, out var nested))
            {
                scrollViewer = nested;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <see cref="TreeView"/> hosts a flat <see cref="TreeViewList"/>; <see cref="ListViewBase.ScrollIntoView(object, ScrollIntoViewAlignment)"/>
    /// scrolls off-screen nodes reliably, while <see cref="TreeViewItem.StartBringIntoView"/> alone often does not under virtualization.
    /// </summary>
    private static TreeViewList? TryFindDescendantTreeViewList(DependencyObject root)
    {
        if (root is TreeViewList list)
            return list;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (TryFindDescendantTreeViewList(child) is { } nested)
                return nested;
        }

        return null;
    }

    private bool TryGetFolderTreeScrollViewer(out ScrollViewer? scrollViewer) =>
        TryFindDescendantScrollViewer(FolderTree, out scrollViewer);

    private bool TryScrollFolderTreeToTop()
    {
        if (!TryGetFolderTreeScrollViewer(out var sv) || sv is null)
            return false;
        sv.ChangeView(sv.HorizontalOffset, 0, null);
        return true;
    }

    private bool TryBringFolderTreeNodeToTop(TreeViewNode node)
    {
        var progressed = false;
        if (TryFindDescendantTreeViewList(FolderTree) is { } flatList)
        {
            flatList.ScrollIntoView(node, ScrollIntoViewAlignment.Leading);
            progressed = true;
        }

        if (FolderTree.ContainerFromNode(node) is not TreeViewItem item)
            return progressed;

        item.StartBringIntoView(
            new BringIntoViewOptions
            {
                AnimationDesired = false,
                VerticalAlignmentRatio = 0,
                HorizontalAlignmentRatio = 0,
            });
        return true;
    }

    private string? TryGetBrowseTreeSelectedFolderPath() => GetFolderPath(TryGetBrowserTreePrimaryNavNode());

    /// <summary>
    /// Directory that defines browse context for tree viewport alignment (same hints as folder navigation).
    /// </summary>
    private string? ResolveBrowsedFolderPathForBrowserTreeViewport()
    {
        if (string.IsNullOrEmpty(_currentFolderPath) || !Directory.Exists(_currentFolderPath))
            return null;

        var primary = TryGetBrowserTreePrimaryNavNode();
        var selPath = primary?.Content is ImageRow sr ? sr.FullPath : null;
        var browsed = BrowseContextDirectory.Resolve(
            _browseNavAnchorPath,
            selPath,
            _currentImageFullPath,
            _currentFolderPath,
            TryGetBrowseTreeSelectedFolderPath());
        if (string.IsNullOrEmpty(browsed))
            browsed = _currentFolderPath;

        if (!IsSameOrDescendantDirectory(_currentFolderPath, browsed))
            browsed = _currentFolderPath;

        return browsed;
    }

    /// <summary>Folder path to pin for viewport alignment after browse selection commit (wizard refresh, undo).</summary>
    private string? GetBrowserTreeViewportPinPathAfterBrowseCommit()
    {
        var browsed = ResolveBrowsedFolderPathForBrowserTreeViewport();
        if (!string.IsNullOrEmpty(browsed) && Directory.Exists(browsed))
            return browsed;
        if (!string.IsNullOrEmpty(_currentFolderPath) && Directory.Exists(_currentFolderPath))
            return _currentFolderPath;
        return null;
    }

    /// <summary>
    /// After the folder tree is mutated or repopulated: pin the browsed folder row to the top of the viewport when
    /// that row exists in the tree; otherwise pin <see cref="TreeView.SelectedNode"/> (e.g. root-level images, where
    /// there is no tree row for <see cref="_currentFolderPath"/> itself). When <paramref name="pinFolderRowPath"/> is
    /// not set, exhaustion uses a deferred scroll attempt while a browse root exists; scroll to offset zero is used
    /// only when there is no active browse directory. Retries when containers are not yet realized.
    /// </summary>
    /// <param name="pinFolderRowPath">When set (e.g. sibling-folder navigation, wizard refresh), that folder row is tried first and exhaustion uses the pinned-folder deferral path.</param>
    private void ScheduleBrowserTreeViewportAfterMutation(string? pinFolderRowPath = null) =>
        _ = ScheduleBrowserTreeViewportAfterMutationAsync(pinFolderRowPath);

    /// <summary>Schedules the same work as <see cref="ScheduleBrowserTreeViewportAfterMutation"/> and completes when the scroll pass finishes (including deferred pinned-folder retries).</summary>
    /// <param name="preferTreeSelectionBeforeBrowsedFolder">When true, try <see cref="TreeView.SelectedNode"/> before the pinned folder row and before the browsed-folder row (e.g. find-in-tree file hits). A successful pin scroll alone does not complete the pass; intermediate pin scrolls may run to help realize <see cref="TreeViewItem"/> containers before retries.</param>
    private Task ScheduleBrowserTreeViewportAfterMutationAsync(
        string? pinFolderRowPath = null,
        bool preferTreeSelectionBeforeBrowsedFolder = false)
    {
        var dq = FolderTree.DispatcherQueue;
        if (dq == null)
            return Task.CompletedTask;

        TaskCompletionSource waiter;
        var startDrain = false;
        lock (_browserTreeViewportCoalesceLock)
        {
            if (!string.IsNullOrEmpty(pinFolderRowPath))
                _browserTreeViewportCoalescedPinFolderPath = pinFolderRowPath;
            if (preferTreeSelectionBeforeBrowsedFolder)
                _browserTreeViewportCoalescedPreferSelectionBeforeBrowsed = true;
            waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _browserTreeViewportCoalesceWaiters.Add(waiter);
            if (_browserTreeViewportCoalesceScheduled)
                return waiter.Task;

            _browserTreeViewportCoalesceScheduled = true;
            startDrain = true;
        }

        if (startDrain
            && !dq.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                StartDrainBrowserTreeViewportCoalescedBatches))
        {
            lock (_browserTreeViewportCoalesceLock)
            {
                _browserTreeViewportCoalesceScheduled = false;
                _browserTreeViewportCoalesceWaiters.Remove(waiter);
            }

            waiter.TrySetResult();
        }

        return waiter.Task;
    }

    private void StartDrainBrowserTreeViewportCoalescedBatches() =>
        _ = DrainBrowserTreeViewportCoalescedBatchesAsync();

    private async Task DrainBrowserTreeViewportCoalescedBatchesAsync()
    {
        try
        {
            for (;;)
            {
                string? pin;
                var preferSelectionBeforeBrowsed = false;
                TaskCompletionSource[] batch;
                lock (_browserTreeViewportCoalesceLock)
                {
                    pin = _browserTreeViewportCoalescedPinFolderPath;
                    _browserTreeViewportCoalescedPinFolderPath = null;
                    preferSelectionBeforeBrowsed = _browserTreeViewportCoalescedPreferSelectionBeforeBrowsed;
                    _browserTreeViewportCoalescedPreferSelectionBeforeBrowsed = false;
                    if (_browserTreeViewportCoalesceWaiters.Count == 0)
                    {
                        _browserTreeViewportCoalesceScheduled = false;
                        return;
                    }

                    batch = _browserTreeViewportCoalesceWaiters.ToArray();
                    _browserTreeViewportCoalesceWaiters.Clear();
                }

                await ExecuteBrowserTreeViewportAfterMutationCoreAsync(pin, preferSelectionBeforeBrowsed)
                    .ConfigureAwait(true);
                foreach (var w in batch)
                    w.TrySetResult();
            }
        }
        catch
        {
            lock (_browserTreeViewportCoalesceLock)
            {
                foreach (var w in _browserTreeViewportCoalesceWaiters)
                    w.TrySetResult();
                _browserTreeViewportCoalesceWaiters.Clear();
                _browserTreeViewportCoalesceScheduled = false;
            }

            throw;
        }
    }

    /// <summary>Single scroll pass: pin browsed folder or selection (see <see cref="ScheduleBrowserTreeViewportAfterMutation"/>).</summary>
    private Task ExecuteBrowserTreeViewportAfterMutationCoreAsync(
        string? pinFolderRowPath,
        bool preferTreeSelectionBeforeBrowsedFolder)
    {
        var dq = FolderTree.DispatcherQueue;
        if (dq == null)
            return Task.CompletedTask;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pinSet = !string.IsNullOrEmpty(pinFolderRowPath);

        void TryComplete() => tcs.TrySetResult();

        void TryStep(int attempt)
        {
            const int maxAttemptsUnpinned = 8;
            const int maxAttemptsPinned = 16;
            var maxAttempts = pinSet ? maxAttemptsPinned : maxAttemptsUnpinned;
            if (string.IsNullOrEmpty(_currentFolderPath) || !Directory.Exists(_currentFolderPath))
            {
                TryComplete();
                return;
            }

            FolderTree.UpdateLayout();

            if (preferTreeSelectionBeforeBrowsedFolder)
            {
                if (FolderTree.SelectedNode is { } selPick && TryBringFolderTreeNodeToTop(selPick))
                {
                    TryComplete();
                    return;
                }

                if (pinSet
                    && Directory.Exists(pinFolderRowPath!)
                    && TryResolveFolderTreeNodeForPath(pinFolderRowPath!) is { } pinNode
                    && pinNode.Content is FolderTreeEntry)
                {
                    _ = TryBringFolderTreeNodeToTop(pinNode);
                }

                var browsedPathPrefer = ResolveBrowsedFolderPathForBrowserTreeViewport();
                if (!string.IsNullOrEmpty(browsedPathPrefer))
                {
                    var folderNodePrefer = TryResolveFolderTreeNodeForPath(browsedPathPrefer);
                    if (folderNodePrefer?.Content is FolderTreeEntry && TryBringFolderTreeNodeToTop(folderNodePrefer))
                    {
                        TryComplete();
                        return;
                    }
                }

                if (FolderTree.SelectedNode is { } selAfterBrowsedPrefer
                    && TryBringFolderTreeNodeToTop(selAfterBrowsedPrefer))
                {
                    TryComplete();
                    return;
                }
            }
            else
            {
                if (pinSet
                    && Directory.Exists(pinFolderRowPath!)
                    && TryResolveFolderTreeNodeForPath(pinFolderRowPath!) is { } pinNode
                    && pinNode.Content is FolderTreeEntry
                    && TryBringFolderTreeNodeToTop(pinNode))
                {
                    TryComplete();
                    return;
                }

                var browsedPath = ResolveBrowsedFolderPathForBrowserTreeViewport();
                if (string.IsNullOrEmpty(browsedPath))
                {
                    TryComplete();
                    return;
                }

                var folderNode = TryResolveFolderTreeNodeForPath(browsedPath);
                if (folderNode?.Content is FolderTreeEntry && TryBringFolderTreeNodeToTop(folderNode))
                {
                    TryComplete();
                    return;
                }

                if (FolderTree.SelectedNode is { } selected && TryBringFolderTreeNodeToTop(selected))
                {
                    TryComplete();
                    return;
                }
            }

            if (attempt >= maxAttempts - 1)
            {
                if (!pinSet)
                {
                    if (preferTreeSelectionBeforeBrowsedFolder
                        && FolderTree.SelectedNode is { } lastSel
                        && TryBringFolderTreeNodeToTop(lastSel))
                    {
                        TryComplete();
                        return;
                    }

                    if (string.IsNullOrEmpty(_currentFolderPath) || !Directory.Exists(_currentFolderPath))
                    {
                        _ = TryScrollFolderTreeToTop();
                        TryComplete();
                        return;
                    }

                    if (!dq.TryEnqueue(
                        Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal,
                        () =>
                        {
                            try
                            {
                                if (string.IsNullOrEmpty(_currentFolderPath) || !Directory.Exists(_currentFolderPath))
                                    return;
                                FolderTree.UpdateLayout();
                                if (preferTreeSelectionBeforeBrowsedFolder)
                                {
                                    if (FolderTree.SelectedNode is { } selUnpinned
                                        && TryBringFolderTreeNodeToTop(selUnpinned))
                                        return;
                                    if (Directory.Exists(pinFolderRowPath!)
                                        && TryResolveFolderTreeNodeForPath(pinFolderRowPath!) is { } pinUnpinned
                                        && pinUnpinned.Content is FolderTreeEntry)
                                        _ = TryBringFolderTreeNodeToTop(pinUnpinned);
                                    var browsedUnpinnedPref = ResolveBrowsedFolderPathForBrowserTreeViewport();
                                    if (!string.IsNullOrEmpty(browsedUnpinnedPref)
                                        && TryResolveFolderTreeNodeForPath(browsedUnpinnedPref) is
                                            { Content: FolderTreeEntry } fnUnpinnedPref
                                        && TryBringFolderTreeNodeToTop(fnUnpinnedPref))
                                        return;
                                    if (FolderTree.SelectedNode is { } selUnpinned2
                                        && TryBringFolderTreeNodeToTop(selUnpinned2))
                                        return;
                                }
                                else
                                {
                                    var browsedUnpinned = ResolveBrowsedFolderPathForBrowserTreeViewport();
                                    if (!string.IsNullOrEmpty(browsedUnpinned)
                                        && TryResolveFolderTreeNodeForPath(browsedUnpinned) is
                                            { Content: FolderTreeEntry } fnUnpinned
                                        && TryBringFolderTreeNodeToTop(fnUnpinned))
                                        return;
                                    if (FolderTree.SelectedNode is { } selUnpinned3
                                        && TryBringFolderTreeNodeToTop(selUnpinned3))
                                        return;
                                }
                            }
                            finally
                            {
                                TryComplete();
                            }
                        }))
                    {
                        TryComplete();
                    }

                    return;
                }

                if (!dq.TryEnqueue(
                        Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal,
                        () =>
                        {
                            try
                            {
                                if (string.IsNullOrEmpty(_currentFolderPath) || !Directory.Exists(_currentFolderPath))
                                    return;
                                FolderTree.UpdateLayout();
                                if (preferTreeSelectionBeforeBrowsedFolder)
                                {
                                    if (FolderTree.SelectedNode is { } selPick2
                                        && TryBringFolderTreeNodeToTop(selPick2))
                                        return;
                                    if (Directory.Exists(pinFolderRowPath!)
                                        && TryResolveFolderTreeNodeForPath(pinFolderRowPath!) is { } pinNode2
                                        && pinNode2.Content is FolderTreeEntry)
                                        _ = TryBringFolderTreeNodeToTop(pinNode2);
                                    var browsedDeferPref = ResolveBrowsedFolderPathForBrowserTreeViewport();
                                    if (!string.IsNullOrEmpty(browsedDeferPref)
                                        && TryResolveFolderTreeNodeForPath(browsedDeferPref) is { Content: FolderTreeEntry } fnDeferPref
                                        && TryBringFolderTreeNodeToTop(fnDeferPref))
                                        return;
                                    if (FolderTree.SelectedNode is { } selPick3
                                        && TryBringFolderTreeNodeToTop(selPick3))
                                        return;
                                }
                                else
                                {
                                    if (Directory.Exists(pinFolderRowPath!)
                                        && TryResolveFolderTreeNodeForPath(pinFolderRowPath!) is { } pinNode2
                                        && pinNode2.Content is FolderTreeEntry
                                        && TryBringFolderTreeNodeToTop(pinNode2))
                                        return;
                                    var browsed = ResolveBrowsedFolderPathForBrowserTreeViewport();
                                    if (!string.IsNullOrEmpty(browsed)
                                        && TryResolveFolderTreeNodeForPath(browsed) is { Content: FolderTreeEntry } fn
                                        && TryBringFolderTreeNodeToTop(fn))
                                        return;
                                    if (FolderTree.SelectedNode is { } sel && TryBringFolderTreeNodeToTop(sel))
                                        return;
                                }
                            }
                            finally
                            {
                                TryComplete();
                            }
                        }))
                {
                    TryComplete();
                }

                return;
            }

            if (!dq.TryEnqueue(
                    Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    () => TryStep(attempt + 1)))
            {
                TryComplete();
            }
        }

        if (!dq.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => TryStep(0)))
        {
            TryComplete();
        }

        return tcs.Task;
    }

    internal async Task RefreshBrowserPaneAfterWizardImageDeletesAsync(
        IReadOnlyList<WizardPredeletedFileStat> succeeded,
        BrowserTreeRefocusAfterWizardContext? refocusContext = null)
    {
        EnterBrowserPaneMutation();
        try
        {
            _ = Interlocked.Increment(ref _populateBrowserGeneration);

            var affectedParentDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (succeeded.Count > 0)
            {
                ApplySuccessfulWizardDeletesToIndexedFolderAggregates(succeeded);
                var resortPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in succeeded)
                {
                    CollectIndexedAncestorPathsForResort(s.FullPath, resortPaths);
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

                RequestCoalescedFolderResortForTouchedFolderPaths(resortPaths);

                foreach (var d in affectedParentDirs)
                    EnqueueFolderMetricsScanIfNeeded(d, FolderMetricsScanScope.ImmediateChildren);
            }

            var synchronizedSiblingLists = new HashSet<IList<TreeViewNode>>(ResortSiblingListReferenceEqualityComparer.Instance);
            await SynchronizeCurrentFolderImageNodesWithDiskAsync(forceStructuralImageRebuild: true).ConfigureAwait(true);
            synchronizedSiblingLists.Add(FolderTree.RootNodes);

            foreach (var d in affectedParentDirs)
            {
                if (string.IsNullOrEmpty(_currentFolderPath) || SameDirectoryPath(d, _currentFolderPath))
                    continue;
                if (!Directory.Exists(d))
                    continue;
                var folderNode = FindFolderTreeNodeByPath(FolderTree.RootNodes, d);
                if (folderNode == null || folderNode.Children.Count == 0)
                    continue;
                if (synchronizedSiblingLists.Contains(folderNode.Children))
                    continue;
                synchronizedSiblingLists.Add(folderNode.Children);
                await SynchronizeFolderImageRowsWithDiskAsync(d, folderNode.Children, updateTransientStatus: false)
                    .ConfigureAwait(true);
            }

            await CommitIncrementalFolderPreviewAndSelectionAsync(refocusContext).ConfigureAwait(true);
            PrepareBrowserTreeViewportAfterWizardMutation();
            await ScheduleBrowserTreeViewportAfterMutationAsync(GetBrowserTreeViewportPinPathAfterBrowseCommit())
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

            var stats = new List<WizardRestoredFileStat>();
            foreach (var p in restoredPaths)
            {
                try
                {
                    if (!File.Exists(p))
                        continue;
                    var fi = new FileInfo(p);
                    stats.Add(new WizardRestoredFileStat(p, fi.Length, ImageExtensions.IsImageFile(p)));
                }
                catch
                {
                    // ignored
                }
            }

            var affectedParentDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (stats.Count > 0)
            {
                ApplyRestoredWizardFilesToIndexedFolderAggregates(stats);
                var resortPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in stats)
                {
                    CollectIndexedAncestorPathsForResort(s.FullPath, resortPaths);
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

                RequestCoalescedFolderResortForTouchedFolderPaths(resortPaths);

                foreach (var d in affectedParentDirs)
                    EnqueueFolderMetricsScanIfNeeded(d, FolderMetricsScanScope.ImmediateChildren);
            }

            var synchronizedSiblingListsUndo = new HashSet<IList<TreeViewNode>>(ResortSiblingListReferenceEqualityComparer.Instance);
            await SynchronizeCurrentFolderImageNodesWithDiskAsync(forceStructuralImageRebuild: true).ConfigureAwait(true);
            synchronizedSiblingListsUndo.Add(FolderTree.RootNodes);

            foreach (var d in affectedParentDirs)
            {
                if (string.IsNullOrEmpty(_currentFolderPath) || SameDirectoryPath(d, _currentFolderPath))
                    continue;
                if (!Directory.Exists(d))
                    continue;
                var folderNode = FindFolderTreeNodeByPath(FolderTree.RootNodes, d);
                if (folderNode == null || folderNode.Children.Count == 0)
                    continue;
                if (synchronizedSiblingListsUndo.Contains(folderNode.Children))
                    continue;
                synchronizedSiblingListsUndo.Add(folderNode.Children);
                await SynchronizeFolderImageRowsWithDiskAsync(d, folderNode.Children, updateTransientStatus: false)
                    .ConfigureAwait(true);
            }

            await CommitIncrementalFolderPreviewAndSelectionAsync(null).ConfigureAwait(true);
            PrepareBrowserTreeViewportAfterWizardMutation();
            await ScheduleBrowserTreeViewportAfterMutationAsync(GetBrowserTreeViewportPinPathAfterBrowseCommit())
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
        if (string.IsNullOrEmpty(_currentFolderPath) || !Directory.Exists(_currentFolderPath))
            return;

        await SynchronizeFolderImageRowsWithDiskAsync(
                _currentFolderPath,
                FolderTree.RootNodes,
                updateTransientStatus: true,
                refreshExistingFileMetadata: false,
                forceStructuralImageRebuild: forceStructuralImageRebuild)
            .ConfigureAwait(true);
    }

    /// <summary>Reconciles <see cref="ImageRow"/> nodes under <paramref name="children"/> with images on disk for <paramref name="folderPath"/>.</summary>
    private async Task SynchronizeFolderImageRowsWithDiskAsync(
        string folderPath,
        IList<TreeViewNode> children,
        bool updateTransientStatus,
        bool refreshExistingFileMetadata = false,
        bool forceStructuralImageRebuild = false)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            return;

        IReadOnlyList<FileSystemEntry> entries;
        try
        {
            entries = await AppServices.FileSystem.ListDirectoryAsync(folderPath).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (updateTransientStatus)
                SetTransientStatus(ex.Message);
            return;
        }

        var imageEntries = entries
            .Where(x => !x.IsDirectory && ImageExtensions.IsImageFile(x.FullPath))
            .ToList();

        var rows = new List<ImageRow>(imageEntries.Count);
        foreach (var e in imageEntries)
            rows.Add(CreateImageRowFromEntry(e));
        rows = ApplyListSort(rows).ToList();
        rows = rows
            .GroupBy(r => r.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var imageChildrenForRefScan = new List<TreeViewNode>();
        var distinctSiblingImagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in children)
        {
            if (child.Content is not ImageRow ir)
                continue;
            imageChildrenForRefScan.Add(child);
            distinctSiblingImagePaths.Add(ir.FullPath);
        }

        var duplicatePathAmongImageSiblings =
            imageChildrenForRefScan.Count != distinctSiblingImagePaths.Count;
        var duplicateNodeRefAmongImageSiblings = false;
        for (var i = 0; i < imageChildrenForRefScan.Count && !duplicateNodeRefAmongImageSiblings; i++)
        {
            for (var j = i + 1; j < imageChildrenForRefScan.Count; j++)
            {
                if (ReferenceEquals(imageChildrenForRefScan[i], imageChildrenForRefScan[j]))
                {
                    duplicateNodeRefAmongImageSiblings = true;
                    break;
                }
            }
        }

        var duplicateImageSiblingsAmongChildren =
            duplicatePathAmongImageSiblings || duplicateNodeRefAmongImageSiblings;

        var folderNodes = new List<TreeViewNode>();
        var imageNodeByPath = new Dictionary<string, TreeViewNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in children)
        {
            if (child.Content is FolderTreeEntry)
                folderNodes.Add(child);
            else if (child.Content is ImageRow ir)
                imageNodeByPath[ir.FullPath] = child;
        }

        var imageNodesInDiskOrder = new List<TreeViewNode>(rows.Count);
        foreach (var row in rows)
        {
            if (imageNodeByPath.TryGetValue(row.FullPath, out var node))
            {
                node.Content = row;
                imageNodesInDiskOrder.Add(node);
            }
            else
            {
                imageNodesInDiskOrder.Add(
                    new TreeViewNode { Content = row, HasUnrealizedChildren = false });
            }
        }

        var duplicateRefInDiskOrder = false;
        for (var i = 0; i < imageNodesInDiskOrder.Count && !duplicateRefInDiskOrder; i++)
        {
            for (var j = i + 1; j < imageNodesInDiskOrder.Count; j++)
            {
                if (ReferenceEquals(imageNodesInDiskOrder[i], imageNodesInDiskOrder[j]))
                {
                    duplicateRefInDiskOrder = true;
                    break;
                }
            }
        }

        var identicalStructure = !forceStructuralImageRebuild
            && !duplicateImageSiblingsAmongChildren
            && !duplicateRefInDiskOrder
            && children.Count == folderNodes.Count + imageNodesInDiskOrder.Count;
        if (identicalStructure)
        {
            for (var i = 0; i < folderNodes.Count; i++)
            {
                if (!ReferenceEquals(children[i], folderNodes[i]))
                {
                    identicalStructure = false;
                    break;
                }
            }

            if (identicalStructure)
            {
                var imageOffset = folderNodes.Count;
                for (var i = 0; i < imageNodesInDiskOrder.Count; i++)
                {
                    if (!ReferenceEquals(children[imageOffset + i], imageNodesInDiskOrder[i]))
                    {
                        identicalStructure = false;
                        break;
                    }
                }
            }
        }

        if (!identicalStructure)
        {
            children.Clear();
            foreach (var f in folderNodes)
                children.Add(f);
            foreach (var img in imageNodesInDiskOrder)
                children.Add(img);
        }

        var existingByPath = new Dictionary<string, TreeViewNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in children)
        {
            if (n.Content is ImageRow ir)
                existingByPath[ir.FullPath] = n;
        }

        if (refreshExistingFileMetadata)
        {
            foreach (var e in imageEntries)
            {
                if (!existingByPath.TryGetValue(e.FullPath, out var tnode) || tnode.Content is not ImageRow ir)
                    continue;
                ir.ApplyRefreshedFileStats(
                    e.LengthBytes ?? 0,
                    e.LastWriteTimeUtc ?? DateTimeOffset.MinValue);
            }
        }

        if (!updateTransientStatus)
            return;

        var dirCount = entries.Count(e => e.IsDirectory);
        var flatEntryCount = entries.Count;
        var status = rows.Count == 0 && flatEntryCount > 0
            ? $"0 image(s) · {flatEntryCount} item(s) in folder (none match supported raster extensions)"
            : $"{rows.Count} image(s) · {dirCount} folder(s)";
        SetTransientStatus(status);
    }

    /// <summary>Reorders <see cref="ImageRow"/> <see cref="TreeViewNode"/> siblings under <paramref name="children"/> to match <see cref="ApplyListSort"/> without touching disk.</summary>
    private void ResortImageRowSiblingsInChildren(IList<TreeViewNode> children)
    {
        var imageNodes = new List<TreeViewNode>();
        foreach (var n in children)
        {
            if (n.Content is ImageRow)
                imageNodes.Add(n);
        }

        var sortedRows = ApplyListSort(imageNodes.Select(n => (ImageRow)n.Content!)).ToList();
        var nodeByPath = imageNodes.ToDictionary(
            n => ((ImageRow)n.Content!).FullPath,
            n => n,
            StringComparer.OrdinalIgnoreCase);
        var orderedNodes = new List<TreeViewNode>(sortedRows.Count);
        foreach (var r in sortedRows)
        {
            if (nodeByPath.TryGetValue(r.FullPath, out var node))
                orderedNodes.Add(node);
        }

        if (orderedNodes.Count == imageNodes.Count)
        {
            var alreadySorted = true;
            for (var i = 0; i < orderedNodes.Count; i++)
            {
                if (!ReferenceEquals(imageNodes[i], orderedNodes[i]))
                {
                    alreadySorted = false;
                    break;
                }
            }

            if (alreadySorted)
                return;
        }

        for (var i = children.Count - 1; i >= 0; i--)
        {
            if (children[i].Content is ImageRow)
                children.RemoveAt(i);
        }

        var insertAt = 0;
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i].Content is FolderTreeEntry)
                insertAt = i + 1;
        }

        for (var i = 0; i < orderedNodes.Count; i++)
            children.Insert(insertAt + i, orderedNodes[i]);
    }

    private static void EnsureBrowseTreeAncestorsExpanded(TreeViewNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            p.IsExpanded = true;
    }

    private void ClearImagePreviewAndSelectFolderRow(TreeViewNode folderNode)
    {
        if (_renameTargetNode != null)
            CancelInlineRename(commit: false);

        StopSlideshowSession();

        PreviewImage.Source = null;
        FullscreenImage.Source = null;
        _currentImageFullPath = null;
        _browseNavAnchorPath = null;
        _session.LastSelectedImage = null;
        InvalidatePreviewRequestsAndClearQueue();
        _lastDecodeTargetBoxWidthPx = -1;
        _lastDecodeTargetBoxHeightPx = -1;
        ClearPreviewBitmapPixelSize();
        UpdatePreviewScrollMetrics();
        SyncBrowseTreeSelection(folderNode);
        UpdatePathOverlays();
        UpdateFullscreenMenuEnabled();
        PersistLayout();
    }

    private async Task<bool> TryFocusBrowserOnFolderWithFirstImageAsync(string folderFullPath)
    {
        if (string.IsNullOrEmpty(folderFullPath) || !Directory.Exists(folderFullPath))
            return false;

        var folderNode = FindFolderTreeNodeByPath(FolderTree.RootNodes, folderFullPath);
        if (folderNode == null)
            return false;

        EnsureBrowseTreeAncestorsExpanded(folderNode);
        folderNode.IsExpanded = true;

        if (folderNode.HasUnrealizedChildren || folderNode.Children.Count == 0)
            await PopulateFolderTreeNodeChildrenAsync(folderNode, folderFullPath, populateGen: null).ConfigureAwait(true);

        var imageRows = new List<ImageRow>();
        foreach (var c in folderNode.Children)
        {
            if (c.Content is ImageRow ir)
                imageRows.Add(ir);
        }

        var sorted = ApplyListSort(imageRows).ToList();
        foreach (var row in sorted)
        {
            if (!BrowseNavigationModeFilter.Matches(_sortSession.GetState(row.FullPath), _browseNavigationMode))
                continue;
            var imgNode = FindImageNodeByPath(FolderTree.RootNodes, row.FullPath);
            if (imgNode == null)
                continue;
            EnsureBrowseTreeAncestorsExpanded(imgNode);
            EnqueuePreviewNavigation(row.FullPath, false);
            SyncBrowseTreeSelection(imgNode);
            _session.LastSelectedImage = row.FullPath;
            return true;
        }

        if (folderNode.Content is FolderTreeEntry)
        {
            ClearImagePreviewAndSelectFolderRow(folderNode);
            return true;
        }

        return false;
    }

    private const int SiblingFolderNavMaxDescendantDepth = 64;
    private const int SiblingFolderNavMaxVisitedPaths = 256;

    private async Task<bool> TryFocusFirstMatchingImageUnderFolderNodeRecursiveAsync(
        TreeViewNode folderNode,
        string folderPath,
        int depth,
        int? populateGen,
        HashSet<string> visitedPaths)
    {
        if (depth > SiblingFolderNavMaxDescendantDepth
            || string.IsNullOrEmpty(folderPath)
            || !Directory.Exists(folderPath)
            || visitedPaths.Count >= SiblingFolderNavMaxVisitedPaths)
            return false;

        if (populateGen is { } g0 && g0 != Volatile.Read(ref _populateBrowserGeneration))
            return false;

        string visitKey;
        try
        {
            visitKey = Path.GetFullPath(folderPath);
        }
        catch
        {
            visitKey = folderPath;
        }

        if (!visitedPaths.Add(visitKey))
            return false;

        folderNode.IsExpanded = true;
        if (folderNode.Children.Count == 0)
            await PopulateFolderTreeNodeChildrenAsync(folderNode, folderPath, populateGen).ConfigureAwait(true);

        if (populateGen is { } g1 && g1 != Volatile.Read(ref _populateBrowserGeneration))
            return false;

        if (TrySelectFirstMatchingImageAmongDirectChildren(folderNode.Children))
            return true;

        foreach (var c in folderNode.Children)
        {
            if (c.Content is not FolderTreeEntry fe)
                continue;
            if (await TryFocusFirstMatchingImageUnderFolderNodeRecursiveAsync(
                    c,
                    fe.Path,
                    depth + 1,
                    populateGen,
                    visitedPaths).ConfigureAwait(true))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Picks the first matching <see cref="ImageRow"/> among immediate children only (same ordering as
    /// <see cref="FolderTree.RootNodes"/>), avoiding a depth-first walk that would skip later root-level images
    /// when an earlier root child is a folder.
    /// </summary>
    private bool TrySelectFirstMatchingImageAmongDirectChildren(IEnumerable<TreeViewNode> nodesInOrder)
    {
        foreach (var n in nodesInOrder)
        {
            if (n.Content is not ImageRow r)
                continue;
            if (!BrowseNavigationModeFilter.Matches(_sortSession.GetState(r.FullPath), _browseNavigationMode))
                continue;
            EnqueuePreviewNavigation(r.FullPath, false);
            SyncBrowseTreeSelection(n);
            _session.LastSelectedImage = r.FullPath;
            return true;
        }

        return false;
    }

    private async Task CommitIncrementalFolderPreviewAndSelectionAsync(BrowserTreeRefocusAfterWizardContext? refocusContext)
    {
        if (!string.IsNullOrEmpty(_currentImageFullPath))
        {
            var keepNode = FindImageNodeByPath(FolderTree.RootNodes, _currentImageFullPath);
            if (keepNode != null && File.Exists(_currentImageFullPath))
            {
                EnsureBrowseTreeAncestorsExpanded(keepNode);
                SyncBrowseTreeSelection(keepNode);
                return;
            }
        }

        var preferred = refocusContext?.PreferredNextFolderFullPath;
        if (!string.IsNullOrEmpty(preferred) && Directory.Exists(preferred))
        {
            if (await TryFocusBrowserOnFolderWithFirstImageAsync(preferred).ConfigureAwait(true))
                return;
        }

        if (!string.IsNullOrEmpty(_currentFolderPath) && Directory.Exists(_currentFolderPath))
        {
            var selectedImagePathFromTree = TryGetBrowserTreePrimaryNavNode()?.Content is ImageRow sel ? sel.FullPath : null;
            var resolved = BrowseContextDirectory.Resolve(
                _browseNavAnchorPath ?? _session.LastSelectedImage,
                selectedImagePathFromTree,
                _currentImageFullPath,
                _currentFolderPath,
                TryGetBrowseTreeSelectedFolderPath());
            var pickContextDir = resolved;
            if (string.IsNullOrEmpty(pickContextDir) || !Directory.Exists(pickContextDir))
                pickContextDir = _currentFolderPath;
            else if (!IsSameOrDescendantDirectory(_currentFolderPath, pickContextDir))
                pickContextDir = _currentFolderPath;

            var workHint = refocusContext?.ImageDeletionWorkingFolder;
            if (!string.IsNullOrEmpty(workHint)
                && Directory.Exists(workHint)
                && IsSameOrDescendantDirectory(_currentFolderPath, workHint)
                && SameDirectoryPath(pickContextDir, _currentFolderPath)
                && !SameDirectoryPath(workHint, _currentFolderPath))
                pickContextDir = workHint;

            if (!SameDirectoryPath(pickContextDir, _currentFolderPath))
            {
                var contextFolderNode = FindFolderTreeNodeByPath(FolderTree.RootNodes, pickContextDir);
                if (contextFolderNode != null)
                {
                    EnsureBrowseTreeAncestorsExpanded(contextFolderNode);
                    contextFolderNode.IsExpanded = true;
                    if (contextFolderNode.HasUnrealizedChildren || contextFolderNode.Children.Count == 0)
                        await PopulateFolderTreeNodeChildrenAsync(contextFolderNode, pickContextDir, populateGen: null)
                            .ConfigureAwait(true);
                }
            }
            else
            {
                var browseRootFolderNode = FindFolderTreeNodeByPath(FolderTree.RootNodes, _currentFolderPath);
                if (browseRootFolderNode != null)
                    EnsureBrowseTreeAncestorsExpanded(browseRootFolderNode);
            }

            var nodes = CollectImageNodesForBrowseContextDirectory(pickContextDir);
            var rows = nodes.Select(n => (ImageRow)n.Content!).ToList();
            var sortedAll = ApplyListSort(rows).ToList();

            var anchor = _browseNavAnchorPath ?? _session.LastSelectedImage ?? _currentImageFullPath;
            var startIdx = 0;
            if (!string.IsNullOrEmpty(anchor))
            {
                var ai = sortedAll.FindIndex(r => string.Equals(r.FullPath, anchor, StringComparison.OrdinalIgnoreCase));
                if (ai >= 0)
                    startIdx = ai + 1;
            }

            ImageRow? pick = null;
            for (var j = startIdx; j < sortedAll.Count; j++)
            {
                if (BrowseNavigationModeFilter.Matches(_sortSession.GetState(sortedAll[j].FullPath), _browseNavigationMode))
                {
                    pick = sortedAll[j];
                    break;
                }
            }

            if (pick == null)
            {
                foreach (var r in sortedAll)
                {
                    if (BrowseNavigationModeFilter.Matches(_sortSession.GetState(r.FullPath), _browseNavigationMode))
                    {
                        pick = r;
                        break;
                    }
                }
            }

            if (pick == null && sortedAll.Count > 0)
            {
                const BrowseNavigationMode relaxed = BrowseNavigationMode.AllImages;
                for (var j = startIdx; j < sortedAll.Count; j++)
                {
                    if (BrowseNavigationModeFilter.Matches(_sortSession.GetState(sortedAll[j].FullPath), relaxed))
                    {
                        pick = sortedAll[j];
                        break;
                    }
                }

                if (pick == null)
                {
                    foreach (var r in sortedAll)
                    {
                        if (BrowseNavigationModeFilter.Matches(_sortSession.GetState(r.FullPath), relaxed))
                        {
                            pick = r;
                            break;
                        }
                    }
                }
            }

            if (pick != null)
            {
                var node = FindImageNodeByPath(FolderTree.RootNodes, pick.FullPath);
                if (node != null)
                {
                    EnsureBrowseTreeAncestorsExpanded(node);
                    EnqueuePreviewNavigation(pick.FullPath, false);
                    SyncBrowseTreeSelection(node);
                    _session.LastSelectedImage = pick.FullPath;
                    return;
                }
            }

            var folderNodeForEmptyList = FindFolderTreeNodeByPath(FolderTree.RootNodes, pickContextDir);
            if (folderNodeForEmptyList?.Content is FolderTreeEntry)
            {
                ClearImagePreviewAndSelectFolderRow(folderNodeForEmptyList);
                return;
            }
        }

        ClearImageSelectionAndPreviewCore();
    }

    private void ApplySubtreeRemovalDeltaToIndexedAncestors(string removedFolderFullPath, long subtreeBytes, int subtreeImages)
    {
        var parent = Directory.GetParent(removedFolderFullPath)?.FullName;
        if (string.IsNullOrEmpty(parent))
            return;

        var dir = parent;
        while (!string.IsNullOrEmpty(dir))
        {
            if (_folderTreeEntryByPath.TryGetValue(dir, out var fe))
            {
                if (fe.AggregateSizeBytes is long bytes)
                    fe.SetAggregateSize(Math.Max(0, bytes - subtreeBytes));
                _folderAggregateBytesByPath[dir] = fe.AggregateSizeBytes;

                if (subtreeImages > 0 && fe.ImageFileCount is int ic)
                    fe.SetImageFileCount(Math.Max(0, ic - subtreeImages));
                _folderImageFileCountByPath[dir] = fe.ImageFileCount;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }
    }

    internal bool TryRemoveFolderTreeNodeByPath(string folderFullPath)
    {
        if (string.IsNullOrEmpty(folderFullPath))
            return false;

        var node = FindFolderTreeNodeByPath(FolderTree.RootNodes, folderFullPath);
        if (node == null)
            return false;

        long subtreeBytes = 0;
        var subtreeImages = 0;
        var hadSubtreeMetrics = false;
        if (node.Content is FolderTreeEntry rootFe
            && rootFe.AggregateSizeBytes is long agg)
        {
            subtreeBytes = agg;
            hadSubtreeMetrics = true;
            if (rootFe.ImageFileCount is int imgc)
                subtreeImages = imgc;
        }

        foreach (var n in EnumerateNodesDepthFirst(node).ToList())
        {
            if (n.Content is FolderTreeEntry fe)
            {
                _folderTreeEntryByPath.Remove(fe.Path);
                UnregisterFolderTreeNodeIndex(fe.Path);
                _folderAggregateBytesByPath.Remove(fe.Path);
                _folderImageFileCountByPath.Remove(fe.Path);
            }
        }

        if (node.Parent != null)
            node.Parent.Children.Remove(node);
        else
            FolderTree.RootNodes.Remove(node);

        if (hadSubtreeMetrics && subtreeBytes > 0)
            ApplySubtreeRemovalDeltaToIndexedAncestors(folderFullPath, subtreeBytes, subtreeImages);

        var parentPath = Directory.GetParent(folderFullPath)?.FullName;
        if (!string.IsNullOrEmpty(parentPath))
        {
            RequestCoalescedFolderResortForTouchedFolderPaths(new[] { parentPath });
            if (!hadSubtreeMetrics || subtreeBytes == 0)
                EnqueueFolderMetricsScanIfNeeded(parentPath, FolderMetricsScanScope.ImmediateChildren);
        }

        return true;
    }

    private void EnqueueFolderMetricsSnapshotForUiApply(string path, FolderMetricsSnapshot snap, int gen)
    {
        var schedule = false;
        lock (_pendingFolderMetricsSnapLock)
        {
            _pendingFolderMetricsSnapshots.Add((path, snap, gen));
            if (!_folderMetricsUiFlushPending)
            {
                _folderMetricsUiFlushPending = true;
                schedule = true;
            }
        }

        if (schedule && !DispatcherQueue.TryEnqueue(ProcessPendingFolderMetricsSnapshotsBatched))
        {
            lock (_pendingFolderMetricsSnapLock)
                _folderMetricsUiFlushPending = false;
        }
    }

    private void ProcessPendingFolderMetricsSnapshotsBatched()
    {
        var resortPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var chunk = 0; chunk < BrowserMetricsSnapshotApplyMaxChunksPerDispatcherCallback; chunk++)
        {
            List<(string Path, FolderMetricsSnapshot Snap, int Gen)> batch;
            lock (_pendingFolderMetricsSnapLock)
            {
                if (_pendingFolderMetricsSnapshots.Count == 0)
                {
                    _folderMetricsUiFlushPending = false;
                    if (resortPaths.Count > 0)
                        RequestCoalescedFolderResortForTouchedFolderPaths(resortPaths);
                    return;
                }

                var take = Math.Min(BrowserMetricsSnapshotApplyChunkSize, _pendingFolderMetricsSnapshots.Count);
                batch = _pendingFolderMetricsSnapshots.GetRange(0, take);
                _pendingFolderMetricsSnapshots.RemoveRange(0, take);
            }

            foreach (var item in batch)
            {
                if (item.Gen != Volatile.Read(ref _populateBrowserGeneration))
                    continue;
                ApplyFolderMetricsSnapshotCore(item.Path, item.Snap, item.Gen, resortPaths);
            }
        }

        lock (_pendingFolderMetricsSnapLock)
        {
            if (_pendingFolderMetricsSnapshots.Count > 0)
            {
                _ = DispatcherQueue.TryEnqueue(ProcessPendingFolderMetricsSnapshotsBatched);
                return;
            }

            _folderMetricsUiFlushPending = false;
        }

        if (resortPaths.Count > 0)
            RequestCoalescedFolderResortForTouchedFolderPaths(resortPaths);
    }

    private void ApplyFolderMetricsSnapshotCore(string path, FolderMetricsSnapshot snap, int gen, HashSet<string> resortPaths)
    {
        if (gen != Volatile.Read(ref _populateBrowserGeneration))
            return;
        if (!_folderTreeEntryByPath.TryGetValue(path, out var fe))
            return;
        fe.SetAggregateSize(snap.AggregateSizeBytes);
        fe.SetImageFileCount(snap.ImageFileCount);
        _folderAggregateBytesByPath[path] = snap.AggregateSizeBytes;
        _folderImageFileCountByPath[path] = snap.ImageFileCount;
        if (IsDeferredFolderMetricsSort(_layoutState.FolderListSort))
            resortPaths.Add(path);
        ApplyHasUnrealizedChildrenFromImmediateMetricsSnapshot(path, snap, gen);
    }

    private void ApplyHasUnrealizedChildrenFromImmediateMetricsSnapshot(string path, FolderMetricsSnapshot snap, int gen)
    {
        if (snap.ScanScope != FolderMetricsScanScope.ImmediateChildren)
            return;
        if (gen != Volatile.Read(ref _populateBrowserGeneration))
            return;
        if (!_folderTreeNodeByPath.TryGetValue(path, out var node))
            return;
        if (snap.HasExpandableChildren is bool b)
        {
            node.HasUnrealizedChildren = b;
            return;
        }

        var one = new List<(string Path, TreeViewNode Node)> { (path, node) };
        ScheduleBrowseExpandabilityProbeBatch(one, gen);
    }

    internal void ScheduleDeferredBrowserChromeAfterStartup()
    {
        _ = DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            DeferredBrowserChromeAfterStartupStep1);
    }

    private void DeferredBrowserChromeAfterStartupStep1()
    {
        ApplyBrowserFileDetailsChrome();
        _ = DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            DeferredBrowserChromeAfterStartupStep2);
    }

    private void DeferredBrowserChromeAfterStartupStep2()
    {
        ApplyBrowserFolderDetailsChrome();
        _ = DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            DeferredBrowserChromeAfterStartupStep3);
    }

    private void DeferredBrowserChromeAfterStartupStep3()
    {
        SyncPinnedBrowserColumnHeaders();
    }

    internal void RefreshAllFolderEntrySizingDisplays()
    {
        foreach (var n in EnumerateNodesDepthFirst(FolderTree.RootNodes))
        {
            if (n.Content is FolderTreeEntry fe)
                PrepareFolderEntrySizingState(fe);
        }
    }

    internal void EnqueueFolderMetricsForAllVisibleFolderPaths()
    {
        foreach (var n in EnumerateVisibleFolderTreeNodesPreorder(FolderTree.RootNodes))
        {
            if (n.Content is not FolderTreeEntry fe)
                continue;
            var scope = n.IsExpanded ? FolderMetricsScanScope.FullSubtree : FolderMetricsScanScope.ImmediateChildren;
            EnqueueFolderMetricsScanIfNeeded(fe.Path, scope);
        }
    }

    internal void CancelBackgroundFolderMetricsWork()
    {
        _folderMetricsCts?.Cancel();
        _folderMetricsCts?.Dispose();
        _folderMetricsCts = new CancellationTokenSource();
    }

    private void ApplyBrowserFileDetailsChrome()
    {
        foreach (var n in EnumerateNodesDepthFirst(FolderTree.RootNodes))
        {
            if (n.Content is ImageRow row)
                ApplyLayoutFileDetailsToImageRow(row);
        }

        SyncPinnedBrowserColumnHeaders();
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

    internal async Task NavigateToFolderAsync(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return;

        if (!string.IsNullOrEmpty(_currentFolderPath)
            && string.Equals(path, _currentFolderPath, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(_pendingSelectImagePath)
            && !string.IsNullOrEmpty(_currentImageFullPath))
        {
            _pendingSelectImagePath = _currentImageFullPath;
        }

        var gen = Interlocked.Increment(ref _populateBrowserGeneration);
        _lastBrowsePopulateUtc = DateTimeOffset.UtcNow;
        ResetBrowserFolderMetricsState();
        _currentFolderPath = path;
        _session.LastBrowseFolder = path;
        _browseNavAnchorPath = null;
        FolderTree.RootNodes.Clear();
        SyncPinnedBrowserColumnHeaders();
        UpdateBrowserToolbar();

        try
        {
            await PopulateBrowserRootsCoreAsync(path, gen).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (gen != Volatile.Read(ref _populateBrowserGeneration))
                return;
            SetTransientStatus(ex.Message);
        }
    }

    private async Task PopulateBrowserRootsCoreAsync(string path, int gen)
    {
        IReadOnlyList<FileSystemEntry> entries;
        try
        {
            entries = await AppServices.FileSystem.ListDirectoryAsync(path).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
            {
                if (gen != Volatile.Read(ref _populateBrowserGeneration))
                    return Task.CompletedTask;
                SetTransientStatus(ex.Message);
                return Task.CompletedTask;
            }).ConfigureAwait(true);
            return;
        }

        if (gen != Volatile.Read(ref _populateBrowserGeneration))
            return;

        var dirsList = FolderDirectorySort.SortDirectories(
                entries.Where(x => x.IsDirectory),
                _layoutState.FolderListSort,
                _folderAggregateBytesByPath,
                _folderImageFileCountByPath)
            .ToList();

        var imageEntries = entries
            .Where(x => !x.IsDirectory && ImageExtensions.IsImageFile(x.FullPath))
            .ToList();

        var flatEntryCount = entries.Count;

        await RunOnUiAsync(async () =>
        {
            if (gen != Volatile.Read(ref _populateBrowserGeneration))
                return;

            var rows = new List<ImageRow>(imageEntries.Count);
            foreach (var e in imageEntries)
                rows.Add(CreateImageRowFromEntry(e));
            rows = ApplyListSort(rows).ToList();

            var deferMetricsBulk = ShouldStagedBrowserPopulate(dirsList.Count);
            if (deferMetricsBulk)
            {
                await AppendBrowserStagedToTargetAsync(
                    FolderTree.RootNodes,
                    dirsList,
                    rows,
                    gen,
                    gen,
                    flatEntryCount,
                    rootBrowsePopulate: true).ConfigureAwait(true);
            }
            else
            {
                AppendBrowserFolderAndImageNodes(
                    FolderTree.RootNodes,
                    dirsList,
                    rows,
                    gen,
                    deferFolderMetricsBulk: false,
                    directoriesAlreadySorted: true);
                FinalizeBrowserRootPopulateAfterImmediateAppend(gen, flatEntryCount, dirsList.Count, rows);
            }
        }).ConfigureAwait(true);
    }

    private void ScheduleBrowseExpandabilityProbeBatch(
        IReadOnlyList<(string Path, TreeViewNode Node)> targets,
        int browserPopulateGeneration)
    {
        if (targets.Count == 0)
            return;

        var ct = _browseExpandProbeCts.Token;
        var gen = browserPopulateGeneration;
        var snapshot = targets.ToList();

        _ = Task.Run(
            async () =>
            {
                const int chunkSize = 48;
                try
                {
                    for (var i = 0; i < snapshot.Count; i += chunkSize)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (gen != Volatile.Read(ref _populateBrowserGeneration))
                            return;

                        var end = Math.Min(i + chunkSize, snapshot.Count);
                        var updates = new List<(TreeViewNode Node, bool HasChildren)>(end - i);
                        for (var j = i; j < end; j++)
                        {
                            ct.ThrowIfCancellationRequested();
                            var path = snapshot[j].Path;
                            updates.Add((snapshot[j].Node, DirHasExpandableChildren(path)));
                        }

                        var dq = DispatcherQueue;
                        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        if (!dq.TryEnqueue(() =>
                            {
                                try
                                {
                                    if (ct.IsCancellationRequested
                                        || gen != Volatile.Read(ref _populateBrowserGeneration))
                                    {
                                        return;
                                    }

                                    foreach (var (node, hasChildren) in updates)
                                        node.HasUnrealizedChildren = hasChildren;
                                }
                                finally
                                {
                                    tcs.TrySetResult();
                                }
                            }))
                        {
                            tcs.TrySetResult();
                        }

                        await tcs.Task.ConfigureAwait(false);
                        await Task.Delay(1).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    // navigation replaced CTS
                }
            },
            ct);
    }

    private static bool DirHasExpandableChildren(string dirPath)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(dirPath).Take(1).Any();
        }
        catch
        {
            return false;
        }
    }

    internal async Task RefreshBrowserTreeFromSettingsAsync()
    {
        if (string.IsNullOrEmpty(_currentFolderPath))
            return;
        if (!string.IsNullOrEmpty(_currentImageFullPath))
            _pendingSelectImagePath = _currentImageFullPath;
        await NavigateToFolderAsync(_currentFolderPath).ConfigureAwait(true);
    }

    /// <summary>Loads folder + image children under <paramref name="node"/> when empty. When <paramref name="populateGen"/> is set, aborts if browser repopulation superseded it.</summary>
    private async Task PopulateFolderTreeNodeChildrenAsync(TreeViewNode node, string path, int? populateGen)
    {
        if (node.Children.Count > 0)
        {
            node.HasUnrealizedChildren = false;
            return;
        }

        if (!_folderChildLoadInFlight.TryAdd(node, 0))
            return;

        try
        {
            IReadOnlyList<FileSystemEntry> entries;
            try
            {
                entries = await AppServices.FileSystem.ListDirectoryAsync(path).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await RunOnUiAsync(() =>
                {
                    SetTransientStatus(ex.Message);
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
                return;
            }

            if (populateGen is { } g && g != Volatile.Read(ref _populateBrowserGeneration))
                return;

            var dirs = FolderDirectorySort.SortDirectories(
                    entries.Where(x => x.IsDirectory),
                    _layoutState.FolderListSort,
                    _folderAggregateBytesByPath,
                    _folderImageFileCountByPath)
                .ToList();

            var imageEntries = entries
                .Where(x => !x.IsDirectory && ImageExtensions.IsImageFile(x.FullPath))
                .ToList();

            var genApply = populateGen ?? Volatile.Read(ref _populateBrowserGeneration);
            var flatEntryCount = entries.Count;

            await RunOnUiAsync(async () =>
            {
                if (populateGen is { } g2 && g2 != Volatile.Read(ref _populateBrowserGeneration))
                    return;

                if (genApply != Volatile.Read(ref _populateBrowserGeneration))
                    return;

                var rows = new List<ImageRow>(imageEntries.Count);
                foreach (var e in imageEntries)
                    rows.Add(CreateImageRowFromEntry(e));
                rows = ApplyListSort(rows).ToList();

                if (ShouldStagedBrowserPopulate(dirs.Count))
                {
                    await AppendBrowserStagedToTargetAsync(
                        node.Children,
                        dirs,
                        rows,
                        genApply,
                        genApply,
                        flatEntryCount,
                        rootBrowsePopulate: false).ConfigureAwait(true);
                }
                else
                {
                    AppendBrowserFolderAndImageNodes(
                        node.Children,
                        dirs,
                        rows,
                        genApply,
                        deferFolderMetricsBulk: false,
                        directoriesAlreadySorted: true);
                }

                if (populateGen is { } g3 && g3 != Volatile.Read(ref _populateBrowserGeneration))
                    return;
                if (genApply != Volatile.Read(ref _populateBrowserGeneration))
                    return;
                node.HasUnrealizedChildren = false;
            }).ConfigureAwait(true);
        }
        finally
        {
            _folderChildLoadInFlight.TryRemove(node, out _);
        }
    }

    private async void FolderTree_OnExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        var node = args.Node;
            var path = GetFolderPath(node);
            if (IsBrowserPaneMutationInProgress || _renameTargetNode != null)
            {
                if (node.Children.Count > 0)
                {
                    node.HasUnrealizedChildren = false;
                    if (!string.IsNullOrEmpty(path))
                        EnqueueFolderMetricsScanIfNeeded(path, FolderMetricsScanScope.FullSubtree);
                }

                return;
            }

            if (node.Children.Count > 0)
            {
                node.HasUnrealizedChildren = false;
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    await SynchronizeFolderImageRowsWithDiskAsync(
                            path,
                            node.Children,
                            updateTransientStatus: false)
                        .ConfigureAwait(true);
                }

                if (!string.IsNullOrEmpty(path))
                    EnqueueFolderMetricsScanIfNeeded(path, FolderMetricsScanScope.FullSubtree);
                return;
            }

            if (string.IsNullOrEmpty(path))
                return;

            await PopulateFolderTreeNodeChildrenAsync(node, path, populateGen: null).ConfigureAwait(true);

            EnqueueFolderMetricsScanIfNeeded(path, FolderMetricsScanScope.FullSubtree);
    }

    private async void FolderTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (_suppressTreeSelectionPreviewEnqueue)
        {
            if (FolderTree.SelectedNode?.Content is ImageRow row)
            {
                _browseNavAnchorPath = row.FullPath;
                _session.LastSelectedImage = row.FullPath;
            }
            else
            {
                _browseNavAnchorPath = null;
            }

            UpdateFullscreenMenuEnabled();
            return;
        }

        await ApplyBrowserTreeLeadPreviewAsync().ConfigureAwait(true);
    }

    /// <summary>Clears browser image selection and blanks preview; no-op in fullscreen (caller should guard).</summary>
    private void ClearImageSelectionAndPreview()
    {
        if (_isFullscreen)
            return;
        ClearImageSelectionAndPreviewCore();
    }

    /// <summary>Blanks preview and clears tree selection; used when the current file is no longer visible in the tree (e.g. folder collapsed) regardless of fullscreen.</summary>
    /// <param name="reselectAfterClear">When non-null and its content is a folder row, selects that node after clearing preview so keyboard focus stays in the tree.</param>
    private void ClearImageSelectionAndPreviewCore(TreeViewNode? reselectAfterClear = null)
    {
        if (_renameTargetNode != null)
            CancelInlineRename(commit: false);

        StopSlideshowSession();

        PreviewImage.Source = null;
        FullscreenImage.Source = null;
        _currentImageFullPath = null;
        _browseNavAnchorPath = null;
        _session.LastSelectedImage = null;
        InvalidatePreviewRequestsAndClearQueue();
        _lastDecodeTargetBoxWidthPx = -1;
        _lastDecodeTargetBoxHeightPx = -1;
        ClearPreviewBitmapPixelSize();
        UpdatePreviewScrollMetrics();
        UpdatePathOverlays();
        UpdateFullscreenMenuEnabled();
        PersistLayout();

        if (reselectAfterClear?.Content is FolderTreeEntry)
        {
            SyncBrowseTreeSelection(reselectAfterClear);
            _ = FolderTree.Focus(FocusState.Programmatic);
        }
        else
        {
            FolderTree.SelectedNode = null;
        }
    }

    private void FolderTree_OnCollapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        if (args.Node?.Content is FolderTreeEntry feCollapsed)
                EnqueueFolderMetricsScanIfNeeded(feCollapsed.Path, FolderMetricsScanScope.ImmediateChildren);

            PruneBrowserTreeSelectionToVisibleNavRows();

            if (_suppressFolderTreeCollapsedClear)
                return;

            if (Volatile.Read(ref _contentDialogModalDepth) > 0 || IsDeleteArchiveWizardOverlayOpen)
                return;

            if (string.IsNullOrEmpty(_currentImageFullPath))
                return;

            var nodes = CollectVisibleImageNodesPreorder(FolderTree.RootNodes);
            var stillVisible = false;
            foreach (var n in nodes)
            {
                if (n.Content is ImageRow r
                    && string.Equals(r.FullPath, _currentImageFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    stillVisible = true;
                    break;
                }
            }

            if (!stillVisible)
            {
                ClearImageSelectionAndPreviewCore(
                    args.Node?.Content is FolderTreeEntry ? args.Node : null);
            }
    }

    private async void FolderTree_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var item = FindAncestorTreeViewItem(e.OriginalSource as DependencyObject);
        var node = FindNodeForTreeViewItem(item);
        if (node?.Content is not FolderTreeEntry fe)
            return;
        e.Handled = true;
        await NavigateToFolderAsync(fe.Path).ConfigureAwait(true);
    }

    private static TreeViewItem? FindAncestorTreeViewItem(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is TreeViewItem ti)
                return ti;
            d = VisualTreeHelper.GetParent(d);
        }

        return null;
    }

    private TreeViewNode? FindNodeForTreeViewItem(TreeViewItem? target)
    {
        if (target == null)
            return null;
        foreach (var n in EnumerateNodesDepthFirst(FolderTree.RootNodes))
        {
            if (FolderTree.ContainerFromNode(n) == target)
                return n;
        }

        return null;
    }

    private static IEnumerable<TreeViewNode> EnumerateNodesDepthFirst(IList<TreeViewNode> roots)
    {
        foreach (var r in roots)
        {
            foreach (var n in EnumerateNodesDepthFirst(r))
                yield return n;
        }
    }

    private static IEnumerable<TreeViewNode> EnumerateNodesDepthFirst(TreeViewNode root)
    {
        yield return root;
        foreach (var c in root.Children)
        {
            foreach (var n in EnumerateNodesDepthFirst(c))
                yield return n;
        }
    }

    private static bool IsFolderMetricsBranchVisible(TreeViewNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
        {
            if (!p.IsExpanded)
                return false;
        }

        return true;
    }

    private static IEnumerable<TreeViewNode> EnumerateVisibleFolderTreeNodesPreorder(IList<TreeViewNode> roots)
    {
        foreach (var r in roots)
        {
            foreach (var n in EnumerateVisibleFolderTreeNodesPreorder(r))
                yield return n;
        }
    }

    private static IEnumerable<TreeViewNode> EnumerateVisibleFolderTreeNodesPreorder(TreeViewNode node)
    {
        if (node.Content is FolderTreeEntry && IsFolderMetricsBranchVisible(node))
            yield return node;
        if (!node.IsExpanded)
            yield break;
        foreach (var c in node.Children)
        {
            foreach (var n in EnumerateVisibleFolderTreeNodesPreorder(c))
                yield return n;
        }
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

    internal ImageRow? GetSelectedImageRow() =>
        TryGetBrowserTreePrimaryNavNode()?.Content as ImageRow;

    /// <summary>
    /// Path used for sort-flag commands: prefers the committed preview path while slideshow is active, or when the
    /// tree’s selected <see cref="ImageRow"/> disagrees with the preview (stale selection after preview-only navigation);
    /// otherwise uses explicit image row selection when present; otherwise the current preview path.
    /// </summary>
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

    private static List<TreeViewNode> CollectVisibleImageNodesPreorder(IList<TreeViewNode> roots)
    {
        var list = new List<TreeViewNode>();

        void walk(TreeViewNode n)
        {
            if (n.Content is ImageRow)
                list.Add(n);
            if (!n.IsExpanded)
                return;
            foreach (var c in n.Children)
                walk(c);
        }

        foreach (var r in roots)
            walk(r);

        return list;
    }

    private static bool SameDirectoryPath(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <returns><see langword="true"/> if <paramref name="candidateFolder"/> is the same path as or nested under <paramref name="ancestorOrSelfFolder"/>.</returns>
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

    private static TreeViewNode? FindFolderTreeNodeByPath(IList<TreeViewNode> roots, string folderFullPath)
    {
        string target;
        try
        {
            target = Path.GetFullPath(folderFullPath);
        }
        catch
        {
            target = folderFullPath;
        }

        foreach (var n in EnumerateNodesDepthFirst(roots))
        {
            if (n.Content is not FolderTreeEntry fe)
                continue;
            string p;
            try
            {
                p = Path.GetFullPath(fe.Path);
            }
            catch
            {
                p = fe.Path;
            }

            if (string.Equals(p, target, StringComparison.OrdinalIgnoreCase))
                return n;
        }

        return null;
    }

    /// <summary>Tier A path index then depth-first scan (same policy as <see cref="TryGetResortSiblingListForFolderPath"/>).</summary>
    private TreeViewNode? TryResolveFolderTreeNodeForPath(string folderFullPath)
    {
        if (string.IsNullOrEmpty(folderFullPath))
            return null;

        if (_folderTreeNodeByPath.TryGetValue(folderFullPath, out var indexed) && indexed != null)
            return indexed;

        string norm;
        try
        {
            norm = Path.GetFullPath(folderFullPath);
        }
        catch
        {
            norm = folderFullPath;
        }

        if (!string.Equals(norm, folderFullPath, StringComparison.OrdinalIgnoreCase)
            && _folderTreeNodeByPath.TryGetValue(norm, out indexed)
            && indexed != null)
        {
            return indexed;
        }

        return FindFolderTreeNodeByPath(FolderTree.RootNodes, folderFullPath);
    }

    private bool IsFolderRowInSiblingCandidates(TreeViewNode contextNode, TreeViewNode possibleSibling)
    {
        IEnumerable<TreeViewNode> candidates = contextNode.Parent != null
            ? contextNode.Parent.Children
            : FolderTree.RootNodes;

        foreach (var c in candidates)
        {
            if (ReferenceEquals(c, possibleSibling))
                return true;
        }

        return false;
    }

    private TreeViewNode? FindFolderSiblingTreeNodeOrIndexed(TreeViewNode contextNode, string siblingFolderFullPath)
    {
        string targetNorm;
        try
        {
            targetNorm = Path.GetFullPath(siblingFolderFullPath);
        }
        catch
        {
            targetNorm = siblingFolderFullPath;
        }

        if (_folderTreeNodeByPath.TryGetValue(siblingFolderFullPath, out var indexed)
            && indexed != null
            && IsFolderRowInSiblingCandidates(contextNode, indexed))
        {
            return indexed;
        }

        if (!string.Equals(siblingFolderFullPath, targetNorm, StringComparison.OrdinalIgnoreCase)
            && _folderTreeNodeByPath.TryGetValue(targetNorm, out indexed)
            && indexed != null
            && IsFolderRowInSiblingCandidates(contextNode, indexed))
        {
            return indexed;
        }

        return FindFolderSiblingTreeNode(contextNode, siblingFolderFullPath);
    }

    private static List<string> DedupePathsPreserveOrder(IReadOnlyList<string> paths)
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

    private List<TreeViewNode> CollectImageNodesForBrowseContextDirectory(string contextDir)
    {
        var list = new List<TreeViewNode>();
        if (string.IsNullOrEmpty(_currentFolderPath))
            return list;

        if (SameDirectoryPath(contextDir, _currentFolderPath))
        {
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in FolderTree.RootNodes)
            {
                if (n.Content is not ImageRow ir)
                    continue;
                if (!seenPaths.Add(ir.FullPath))
                    continue;
                list.Add(n);
            }

            return list;
        }

        var folderNode = FindFolderTreeNodeByPath(FolderTree.RootNodes, contextDir);
        if (folderNode == null)
            return list;

        var seenChildPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in folderNode.Children)
        {
            if (c.Content is not ImageRow ir)
                continue;
            if (!seenChildPaths.Add(ir.FullPath))
                continue;
            list.Add(c);
        }

        return list;
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

    /// <summary>Ordered browse paths from already-materialized tree rows for <paramref name="contextDir"/> only.</summary>
    private List<string> BuildBrowseNavPathsFromTreeOnly(string contextDir)
    {
        var fullNodes = CollectImageNodesForBrowseContextDirectory(contextDir);
        return BuildFilteredBrowseNavNodesAndPaths(fullNodes).Paths;
    }

    /// <summary>
    /// Ordered image paths for sequential browse in <paramref name="contextDir"/>; uses the tree when populated,
    /// otherwise a single-folder disk listing (same sort and browse-mode filter as the tree).
    /// </summary>
    private async Task<List<string>> GetBrowseNavigationPathsForContextAsync(string contextDir)
    {
        var fromTree = BuildBrowseNavPathsFromTreeOnly(contextDir);
        if (fromTree.Count > 0)
            return DedupePathsPreserveOrder(fromTree);

        if (string.IsNullOrEmpty(_currentFolderPath)
            || string.IsNullOrEmpty(contextDir)
            || !Directory.Exists(contextDir)
            || !BrowseContextImageSequence.IsContextDirectoryUnderBrowseRoot(_currentFolderPath, contextDir))
            return new List<string>();

        IReadOnlyList<FileSystemEntry> entries;
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

    /// <summary>
    /// Expands and populates folder nodes from the browse root down to <paramref name="folderFullPath"/> so tree
    /// rows exist for that directory. When <paramref name="expandAndPopulateDestinationFolder"/> is false, the
    /// folder for the final path segment is not expanded and its children are not loaded; ancestors are still
    /// expanded so the destination folder row remains visible under its parent.
    /// </summary>
    private async Task<bool> TryEnsureFolderPathMaterializedAsync(
        string folderFullPath,
        int? populateGen,
        bool expandAndPopulateDestinationFolder = true)
    {
        if (string.IsNullOrEmpty(_currentFolderPath)
            || string.IsNullOrEmpty(folderFullPath)
            || !Directory.Exists(folderFullPath)
            || !IsSameOrDescendantDirectory(_currentFolderPath, folderFullPath))
            return false;

        if (SameDirectoryPath(folderFullPath, _currentFolderPath))
            return true;

        string rel;
        try
        {
            rel = Path.GetRelativePath(_currentFolderPath, folderFullPath);
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrEmpty(rel) || rel == ".")
            return true;

        var segments = rel.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return true;

        var cur = _currentFolderPath;
        IList<TreeViewNode> siblings = FolderTree.RootNodes;

        for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
        {
            var segment = segments[segmentIndex];
            cur = Path.Combine(cur, segment);
            TreeViewNode? folder = null;
            foreach (var c in siblings)
            {
                if (c.Content is not FolderTreeEntry fe)
                    continue;
                if (SameDirectoryPath(fe.Path, cur))
                {
                    folder = c;
                    break;
                }
            }

            if (folder == null)
                return false;

            EnsureBrowseTreeAncestorsExpanded(folder);
            var isDestinationSegment = segmentIndex == segments.Length - 1;
            if (!isDestinationSegment || expandAndPopulateDestinationFolder)
            {
                folder.IsExpanded = true;
                if (folder.HasUnrealizedChildren || folder.Children.Count == 0)
                    await PopulateFolderTreeNodeChildrenAsync(folder, cur, populateGen).ConfigureAwait(true);
            }

            if (populateGen is { } g && g != Volatile.Read(ref _populateBrowserGeneration))
                return false;

            siblings = folder.Children;
        }

        return true;
    }

    private async Task TrySyncBrowseTreeSelectionToImagePathAsync(string imagePath)
    {
        var dir = Path.GetDirectoryName(imagePath);
        if (string.IsNullOrEmpty(dir))
            return;

        if (!await TryEnsureFolderPathMaterializedAsync(dir, populateGen: null).ConfigureAwait(true))
            return;

        var imgNode = FindImageNodeByPath(FolderTree.RootNodes, imagePath);
        if (imgNode == null)
            return;

        EnsureBrowseTreeAncestorsExpanded(imgNode);
        SyncBrowseTreeSelection(imgNode);
    }

    private static TreeViewNode? FindImageNodeByPath(IList<TreeViewNode> roots, string fullPath)
    {
        foreach (var n in EnumerateNodesDepthFirst(roots))
        {
            if (n.Content is ImageRow r && string.Equals(r.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                return n;
        }

        return null;
    }

    private static TreeViewNode? FirstImageNodePreorder(IList<TreeViewNode> roots)
    {
        foreach (var n in EnumerateNodesDepthFirst(roots))
        {
            if (n.Content is ImageRow)
                return n;
        }

        return null;
    }

    private void RefreshSortFlagDisplayInList(string fullPath)
    {
        foreach (var n in EnumerateNodesDepthFirst(FolderTree.RootNodes))
        {
            if (n.Content is ImageRow r && string.Equals(r.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                ApplySortFlagPresentationToRow(r, fullPath);
                return;
            }
        }
    }

    private void RefreshAllSortFlagDisplaysInList()
    {
        foreach (var n in EnumerateNodesDepthFirst(FolderTree.RootNodes))
        {
            if (n.Content is ImageRow r)
                ApplySortFlagPresentationToRow(r, r.FullPath);
        }
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

    /// <summary>Begin sibling-folder navigation (parent of image context directory, same sort as tree). Does not await.</summary>
    /// <param name="contextDirectoryOverride">When set, used as the current folder for sibling stepping (must be under the browse root); otherwise resolved from selection and preview hints.</param>
    internal void BrowseNavigateSiblingFolderFromInput(int delta, string? contextDirectoryOverride = null)
    {
        if (IsBrowserPaneMutationInProgress)
            return;
        _ = BrowseNavigateSiblingFolderAsync(delta, contextDirectoryOverride);
    }

    private async Task BrowseNavigateSiblingFolderAsync(int delta, string? contextDirectoryOverride = null)
    {
        if (delta != 1 && delta != -1)
            return;

        if (IsBrowserPaneMutationInProgress)
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

        var gen = Volatile.Read(ref _populateBrowserGeneration);
        IReadOnlyList<FileSystemEntry> entries;
        try
        {
            entries = await AppServices.FileSystem.ListDirectoryAsync(parent.FullName).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (gen == Volatile.Read(ref _populateBrowserGeneration))
                SetTransientStatus(ex.Message);
            return;
        }

        if (gen != Volatile.Read(ref _populateBrowserGeneration))
            return;

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
        {
            string trimmedCurrent;
            try
            {
                trimmedCurrent = currentNorm.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                trimmedCurrent = currentNorm;
            }

            for (var i = 0; i < dirs.Count; i++)
            {
                string trimmedDir;
                try
                {
                    trimmedDir = dirs[i].FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch
                {
                    trimmedDir = dirs[i].FullPath;
                }

                if (string.Equals(trimmedDir, trimmedCurrent, StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                    break;
                }
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
            if (string.IsNullOrEmpty(_currentFolderPath) || !SameDirectoryPath(_currentFolderPath, targetFolderPath))
                return;

            var genAfter = Volatile.Read(ref _populateBrowserGeneration);
            var rootsSnapshot = new List<TreeViewNode>(FolderTree.RootNodes.Count);
            foreach (TreeViewNode n in FolderTree.RootNodes)
                rootsSnapshot.Add(n);

            if (TrySelectFirstMatchingImageAmongDirectChildren(rootsSnapshot))
                return;

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rootChild in rootsSnapshot)
            {
                if (rootChild.Content is not FolderTreeEntry fe)
                    continue;
                if (await TryFocusFirstMatchingImageUnderFolderNodeRecursiveAsync(
                        rootChild,
                        fe.Path,
                        depth: 1,
                        genAfter,
                        visited).ConfigureAwait(true))
                    return;
            }

            if (genAfter == Volatile.Read(ref _populateBrowserGeneration))
                SetTransientStatus("No images found in that folder (or none match the current browse filter).");
            ClearImageSelectionAndPreviewCore();
            return;
        }

        await RunOnUiAsync(async () =>
        {
            for (var attempt = 0; attempt < SiblingFolderNavMaterializeMaxAttempts; attempt++)
            {
                if (gen != Volatile.Read(ref _populateBrowserGeneration))
                    return;

                var contextNode = TryResolveFolderTreeNodeForPath(contextDir);
                var siblingNode = contextNode != null
                    ? FindFolderSiblingTreeNodeOrIndexed(contextNode, targetFolderPath)
                    : null;

                if (contextNode != null && siblingNode != null)
                {
                    var folderPath = GetFolderPath(siblingNode);
                    if (string.IsNullOrEmpty(folderPath))
                        return;

                    await PopulateFolderTreeNodeChildrenAsync(siblingNode, folderPath, gen).ConfigureAwait(true);

                    if (gen != Volatile.Read(ref _populateBrowserGeneration))
                        return;

                    siblingNode.IsExpanded = true;

                    var visitedNested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (!await TryFocusFirstMatchingImageUnderFolderNodeRecursiveAsync(
                            siblingNode,
                            folderPath,
                            depth: 0,
                            gen,
                            visitedNested).ConfigureAwait(true))
                    {
                        if (gen == Volatile.Read(ref _populateBrowserGeneration))
                        {
                            if (siblingNode.Children.Count == 0)
                                SetTransientStatus("That folder is empty.");
                            else
                                SetTransientStatus("No images found in that folder (or none match the current browse filter).");
                        }

                        ClearImageSelectionAndPreviewCore();
                    }

                    _suppressFolderTreeCollapsedClear = true;
                    try
                    {
                        contextNode.IsExpanded = false;
                    }
                    finally
                    {
                        _ = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(
                            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                            () => { _suppressFolderTreeCollapsedClear = false; });
                    }

                    FolderTree.UpdateLayout();
                    if (siblingNode.Content is FolderTreeEntry)
                        TryBringFolderTreeNodeToTop(siblingNode);

                    ScheduleBrowserTreeViewportAfterMutation(targetFolderPath);
                    return;
                }

                var browseRoot = _currentFolderPath;
                var mayStillStage = !string.IsNullOrEmpty(browseRoot)
                    && IsSameOrDescendantDirectory(browseRoot, targetFolderPath)
                    && IsSameOrDescendantDirectory(browseRoot, contextDir);

                if (!mayStillStage || attempt == SiblingFolderNavMaterializeMaxAttempts - 1)
                    break;

                await Task.Yield();
            }

            if (gen == Volatile.Read(ref _populateBrowserGeneration)
                && !string.IsNullOrEmpty(_currentFolderPath)
                && BrowseContextImageSequence.IsContextDirectoryUnderBrowseRoot(_currentFolderPath, targetFolderPath))
            {
                if (await TryEnsureFolderPathMaterializedAsync(targetFolderPath, gen).ConfigureAwait(true)
                    && await TryFocusBrowserOnFolderWithFirstImageAsync(targetFolderPath).ConfigureAwait(true))
                {
                    FolderTree.UpdateLayout();
                    if (TryResolveFolderTreeNodeForPath(targetFolderPath) is { Content: FolderTreeEntry } pinFolder)
                        TryBringFolderTreeNodeToTop(pinFolder);

                    ScheduleBrowserTreeViewportAfterMutation(targetFolderPath);
                    return;
                }
            }

            if (gen == Volatile.Read(ref _populateBrowserGeneration))
            {
                SetTransientStatus(
                    "Could not open that sibling folder in the tree yet. Try again after folder rows finish loading.");
            }
        }).ConfigureAwait(true);
    }

    private TreeViewNode? FindFolderSiblingTreeNode(TreeViewNode contextNode, string siblingFolderFullPath)
    {
        string targetNorm;
        try
        {
            targetNorm = Path.GetFullPath(siblingFolderFullPath);
        }
        catch
        {
            targetNorm = siblingFolderFullPath;
        }

        IEnumerable<TreeViewNode> candidates = contextNode.Parent != null
            ? contextNode.Parent.Children
            : FolderTree.RootNodes;

        foreach (var c in candidates)
        {
            if (c.Content is not FolderTreeEntry fe)
                continue;

            string p;
            try
            {
                p = Path.GetFullPath(fe.Path);
            }
            catch
            {
                p = fe.Path;
            }

            if (string.Equals(p, targetNorm, StringComparison.OrdinalIgnoreCase))
                return c;
        }

        return null;
    }

    private void SyncBrowseTreeSelection(TreeViewNode node)
    {
        try
        {
            _suppressTreeSelectionPreviewEnqueue = true;
            FolderTree.SelectedNode = node;
        }
        finally
        {
            _suppressTreeSelectionPreviewEnqueue = false;
        }
    }

    private static bool IsBrowserTreeNavRowNode(TreeViewNode? node) =>
        node?.Content is FolderTreeEntry or ImageRow;

    internal IReadOnlyList<TreeViewNode> GetSelectedBrowserTreeNavNodes()
    {
        var n = FolderTree.SelectedNode;
        if (n != null && IsBrowserTreeNavRowNode(n))
            return new[] { n };

        return Array.Empty<TreeViewNode>();
    }

    private TreeViewNode? TryGetBrowserTreePrimaryNavNode() => FolderTree.SelectedNode;

    private async Task ApplyBrowserTreeLeadPreviewAsync()
    {
        if (FolderTree.SelectedNode?.Content is ImageRow row)
        {
            _browseNavAnchorPath = row.FullPath;
            await OnFolderTreeImageRowSelectedAsync(row).ConfigureAwait(true);
        }
        else
        {
            _browseNavAnchorPath = null;
            if (!_isFullscreen && !string.IsNullOrEmpty(_currentImageFullPath))
                ClearBrowserPaneImagePreviewStatePreserveTreeSelection();
        }

        UpdateFullscreenMenuEnabled();
    }

    /// <summary>Clears preview and current image path state without changing tree selection (folder-only or non-image lead).</summary>
    private void ClearBrowserPaneImagePreviewStatePreserveTreeSelection()
    {
        StopSlideshowSession();
        PreviewImage.Source = null;
        FullscreenImage.Source = null;
        _currentImageFullPath = null;
        _session.LastSelectedImage = null;
        InvalidatePreviewRequestsAndClearQueue();
        _lastDecodeTargetBoxWidthPx = -1;
        _lastDecodeTargetBoxHeightPx = -1;
        ClearPreviewBitmapPixelSize();
        UpdatePreviewScrollMetrics();
        UpdatePathOverlays();
        PersistLayout();
    }

    private void PruneBrowserTreeSelectionToVisibleNavRows()
    {
        var sel = FolderTree.SelectedNode;
        if (sel == null)
            return;

        var visible = new HashSet<TreeViewNode>(EnumerateVisibleBrowserTreeNavRows(FolderTree.RootNodes));
        if (!visible.Contains(sel))
            FolderTree.SelectedNode = null;
    }

    /// <summary>
    /// Keyboard/command browse step: resolve target from scoped folder paths (tree or disk), enqueue preview, then sync tree.
    /// </summary>
    private void BrowseNavigateByStep(BrowseNavStepKind step)
    {
        if (IsBrowserPaneMutationInProgress)
            return;
        IncrementNavCommandCounter();
        _ = BrowseNavigateByStepAsync(step);
    }

    private async Task BrowseNavigateByStepAsync(BrowseNavStepKind step)
    {
        if (IsBrowserPaneMutationInProgress)
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
        var node = FindImageNodeByPath(FolderTree.RootNodes, path);
        if (node != null)
        {
            EnsureBrowseTreeAncestorsExpanded(node);
            SyncBrowseTreeSelection(node);
        }
        else
            await TrySyncBrowseTreeSelectionToImagePathAsync(path).ConfigureAwait(true);
    }

    private static List<string> BuildVisibleImagePathsFromNodes(List<TreeViewNode> nodes)
    {
        var paths = new List<string>(nodes.Count);
        foreach (var n in nodes)
        {
            if (n.Content is ImageRow r)
                paths.Add(r.FullPath);
        }

        return paths;
    }

    private (List<TreeViewNode> Nodes, List<string> Paths) BuildFilteredBrowseNavNodesAndPaths(List<TreeViewNode> fullNodes)
    {
        if (_browseNavigationMode == BrowseNavigationMode.AllImages)
            return (fullNodes, BuildVisibleImagePathsFromNodes(fullNodes));

        var nodes = new List<TreeViewNode>();
        var paths = new List<string>();
        foreach (var n in fullNodes)
        {
            if (n.Content is not ImageRow r)
                continue;
            if (!BrowseNavigationModeFilter.Matches(_sortSession.GetState(r.FullPath), _browseNavigationMode))
                continue;
            nodes.Add(n);
            paths.Add(r.FullPath);
        }

        return (nodes, paths);
    }

    private TreeViewNode? FirstImageNodePreorderMatchingNavMode(IList<TreeViewNode> roots)
    {
        foreach (var n in EnumerateNodesDepthFirst(roots))
        {
            if (n.Content is not ImageRow r)
                continue;
            if (!BrowseNavigationModeFilter.Matches(_sortSession.GetState(r.FullPath), _browseNavigationMode))
                continue;
            return n;
        }

        return null;
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
        if (_browseNavigationMode == mode)
        {
            SyncBrowseNavigationModeMenu();
            return;
        }

        _browseNavigationMode = mode;
        SyncBrowseNavigationModeMenu();
        EnsurePreviewMatchesBrowseNavigationMode();
        UpdatePathOverlays();
    }

    private void SyncBrowseNavigationModeMenu()
    {
        BrowseNavModeAllItem.IsChecked = _browseNavigationMode == BrowseNavigationMode.AllImages;
        BrowseNavModeKeepItem.IsChecked = _browseNavigationMode == BrowseNavigationMode.KeepOnly;
        BrowseNavModeNotKeepItem.IsChecked = _browseNavigationMode == BrowseNavigationMode.NotKeepOnly;
        BrowseNavModeUnflaggedItem.IsChecked = _browseNavigationMode == BrowseNavigationMode.UnflaggedOnly;
        BrowseNavModeDeleteItem.IsChecked = _browseNavigationMode == BrowseNavigationMode.DeleteOnly;
    }

    private void EnsurePreviewMatchesBrowseNavigationMode()
    {
        _ = EnsurePreviewMatchesBrowseNavigationModeAsync();
    }

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
        var node = FindImageNodeByPath(FolderTree.RootNodes, firstPath);
        if (node != null)
            SyncBrowseTreeSelection(node);
        else
            await TrySyncBrowseTreeSelectionToImagePathAsync(firstPath).ConfigureAwait(true);
    }

    private static IEnumerable<TreeViewNode> EnumerateVisibleBrowserTreeNavRows(IList<TreeViewNode> roots)
    {
        foreach (var r in roots)
        {
            foreach (var n in EnumerateVisibleBrowserTreeNavRows(r))
                yield return n;
        }
    }

    private static IEnumerable<TreeViewNode> EnumerateVisibleBrowserTreeNavRows(TreeViewNode node)
    {
        switch (node.Content)
        {
            case FolderTreeEntry:
                if (!IsFolderMetricsBranchVisible(node))
                    yield break;
                yield return node;
                if (!node.IsExpanded)
                    yield break;
                foreach (var c in node.Children)
                {
                    foreach (var x in EnumerateVisibleBrowserTreeNavRows(c))
                        yield return x;
                }

                yield break;
            case ImageRow:
                if (IsFolderMetricsBranchVisible(node))
                    yield return node;
                yield break;
            default:
                if (node.IsExpanded)
                {
                    foreach (var c in node.Children)
                    {
                        foreach (var x in EnumerateVisibleBrowserTreeNavRows(c))
                            yield return x;
                    }
                }

                yield break;
        }
    }

    private void BrowseTreeKeyboardMoveSelection(int delta)
    {
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

        var list = EnumerateVisibleBrowserTreeNavRows(FolderTree.RootNodes).ToList();
        if (list.Count == 0)
            return;

        var idx = sel != null ? list.IndexOf(sel) : -1;
        var next = Math.Clamp(idx + delta, 0, list.Count - 1);
        if (next == idx && sel != null)
            return;

        var target = list[next];
        SyncBrowseTreeSelection(target);
        _ = TryBringFolderTreeNodeToTop(target);
    }

    private void BrowseTreeKeyboardExpandFolderTarget()
    {
        if (!TryGetBrowseTreeKeyboardFolderTarget(out var folderNode))
            return;
        if (!folderNode.IsExpanded)
            folderNode.IsExpanded = true;
    }

    private void BrowseTreeKeyboardCollapseFolderTarget()
    {
        if (!TryGetBrowseTreeKeyboardFolderTarget(out var folderNode))
            return;
        if (folderNode.IsExpanded)
            folderNode.IsExpanded = false;
    }

    private bool TryGetBrowseTreeKeyboardFolderTarget([NotNullWhen(true)] out TreeViewNode? folderNode)
    {
        folderNode = null;
        var sel = TryGetBrowserTreePrimaryNavNode();
        if (sel == null)
            return false;
        if (sel.Content is FolderTreeEntry)
        {
            folderNode = sel;
            return true;
        }

        if (sel.Content is ImageRow)
        {
            var p = sel.Parent;
            if (p?.Content is FolderTreeEntry)
            {
                folderNode = p;
                return true;
            }
        }

        return false;
    }

    private bool IsFocusInsideBrowserTree()
    {
        var focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot!) as DependencyObject;
        while (focused != null)
        {
            if (focused == FolderTree)
                return true;
            focused = VisualTreeHelper.GetParent(focused);
        }

        return false;
    }

    private void FolderTree_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_isFullscreen)
            return;

        if (TryDispatchBrowserTreeInputCommand(e))
            return;

        if (TryDispatchInputCommand(e))
            return;

        if (e.Key == VirtualKey.Enter && TryGetBrowserTreePrimaryNavNode()?.Content is ImageRow)
        {
            TryEnterFullscreen();
            e.Handled = true;
        }
    }

    private bool TryBeginRenameSelectedBrowserItem()
    {
        var node = TryGetBrowserTreePrimaryNavNode();
        if (node?.Content is ImageRow row)
        {
            row.EditingName = row.DisplayName;
            row.IsRenaming = true;
            _renameTargetNode = node;
            return true;
        }

        if (node?.Content is FolderTreeEntry fe)
        {
            fe.EditingName = fe.DisplayLabel;
            fe.IsRenaming = true;
            _renameTargetNode = node;
            return true;
        }

        return false;
    }

    private void BrowserRenameTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            tb.SelectAll();
            _ = tb.DispatcherQueue.TryEnqueue(() => tb.Focus(FocusState.Programmatic));
        }
    }

    private async void BrowserRenameTextBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox tb)
            return;
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await TryCommitInlineRenameFromTextBoxAsync(tb).ConfigureAwait(true);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            CancelInlineRename(commit: false);
        }
    }

    private async void BrowserRenameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_renameCommitInProgress)
            return;
        if (sender is TextBox tb)
            await TryCommitInlineRenameFromTextBoxAsync(tb).ConfigureAwait(true);
    }

    private void CancelInlineRename(bool commit)
    {
        if (!commit && _renameTargetNode?.Content is ImageRow row)
        {
            row.EditingName = row.DisplayName;
            row.IsRenaming = false;
        }
        else if (!commit && _renameTargetNode?.Content is FolderTreeEntry fe)
        {
            fe.EditingName = fe.DisplayLabel;
            fe.IsRenaming = false;
        }

        _renameTargetNode = null;
    }

    private async Task TryCommitInlineRenameFromTextBoxAsync(TextBox tb)
    {
        if (_renameCommitInProgress)
            return;
        var node = _renameTargetNode;
        if (node == null)
            return;
        if (node.Content is ImageRow rowCheck && !rowCheck.IsRenaming)
            return;
        if (node.Content is FolderTreeEntry feCheck && !feCheck.IsRenaming)
            return;

        _renameCommitInProgress = true;
        try
        {
            if (node.Content is ImageRow imageRow)
                await CommitImageRenameAsync(imageRow, tb.Text).ConfigureAwait(true);
            else if (node.Content is FolderTreeEntry folderEntry)
                await CommitFolderRenameAsync(folderEntry, tb.Text).ConfigureAwait(true);
        }
        finally
        {
            _renameCommitInProgress = false;
        }
    }

    private async Task CommitImageRenameAsync(ImageRow row, string newNameInput)
    {
        var trimmed = newNameInput.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            SetTransientStatus("Name cannot be empty.");
            CancelInlineRename(commit: false);
            return;
        }

        var oldPath = row.FullPath;
        var dir = Path.GetDirectoryName(oldPath);
        if (string.IsNullOrEmpty(dir))
        {
            CancelInlineRename(commit: false);
            return;
        }

        var ext = Path.GetExtension(oldPath);
        var desired = trimmed.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + ext;

        var imageSiblingList = _renameTargetNode?.Parent?.Children;

        string destPath;
        try
        {
            destPath = BrowserPaneRenameHelper.PickUniqueFileName(dir, desired, oldPath);
        }
        catch (Exception ex)
        {
            SetTransientStatus(ex.Message);
            CancelInlineRename(commit: false);
            return;
        }

        if (string.Equals(destPath, oldPath, StringComparison.OrdinalIgnoreCase))
        {
            row.IsRenaming = false;
            _renameTargetNode = null;
            return;
        }

        try
        {
            File.Move(oldPath, destPath);
        }
        catch (Exception ex)
        {
            SetTransientStatus("Rename failed: " + ex.Message);
            CancelInlineRename(commit: false);
            return;
        }

        var fi = new FileInfo(destPath);
        row.ApplyRenamedPath(destPath, fi.Name, fi.Length, new DateTimeOffset(fi.LastWriteTimeUtc));
        row.IsRenaming = false;
        _renameTargetNode = null;

        if (string.Equals(_session.LastSelectedImage, oldPath, StringComparison.OrdinalIgnoreCase))
            _session.LastSelectedImage = destPath;
        if (string.Equals(_currentImageFullPath, oldPath, StringComparison.OrdinalIgnoreCase))
            _currentImageFullPath = destPath;
        if (string.Equals(_browseNavAnchorPath, oldPath, StringComparison.OrdinalIgnoreCase))
            _browseNavAnchorPath = destPath;

        if (imageSiblingList != null)
            ResortImageRowSiblingsInChildren(imageSiblingList);

        EnqueueFolderMetricsRescan(dir, FolderMetricsScanScope.ImmediateChildren);

        UpdatePathOverlays();
        PersistLayout();
        SetTransientStatus("Renamed.");
        await Task.Yield();
    }

    private async Task CommitFolderRenameAsync(FolderTreeEntry folderEntry, string newNameInput)
    {
        var trimmed = newNameInput.TrimEnd();
        if (string.IsNullOrEmpty(trimmed))
        {
            SetTransientStatus("Name cannot be empty.");
            CancelInlineRename(commit: false);
            return;
        }

        var oldPath = folderEntry.Path;
        var parent = Path.GetDirectoryName(oldPath);
        if (string.IsNullOrEmpty(parent))
        {
            CancelInlineRename(commit: false);
            return;
        }

        var resolvedTreeNode = _renameTargetNode ?? FindFolderTreeNodeByPath(FolderTree.RootNodes, oldPath);
        var wasBrowseRoot = !string.IsNullOrEmpty(_currentFolderPath) && SameDirectoryPath(_currentFolderPath, oldPath);

        string destPath;
        try
        {
            destPath = BrowserPaneRenameHelper.PickUniqueDirectoryName(parent, trimmed, oldPath);
        }
        catch (Exception ex)
        {
            SetTransientStatus(ex.Message);
            CancelInlineRename(commit: false);
            return;
        }

        if (string.Equals(destPath, oldPath, StringComparison.OrdinalIgnoreCase))
        {
            folderEntry.IsRenaming = false;
            _renameTargetNode = null;
            return;
        }

        try
        {
            Directory.Move(oldPath, destPath);
        }
        catch (Exception ex)
        {
            SetTransientStatus("Rename failed: " + ex.Message);
            CancelInlineRename(commit: false);
            return;
        }

        folderEntry.IsRenaming = false;
        _renameTargetNode = null;

        if (!string.IsNullOrEmpty(_currentFolderPath)
            && string.Equals(_currentFolderPath, oldPath, StringComparison.OrdinalIgnoreCase))
        {
            _currentFolderPath = destPath;
            _session.LastBrowseFolder = destPath;
        }

        if (resolvedTreeNode != null)
        {
            ApplyRenamedFolderSubtreeInPlace(resolvedTreeNode, oldPath, destPath, folderEntry);
            return;
        }

        if (wasBrowseRoot)
        {
            ApplyBrowseRootFolderRelocatedInPlace(oldPath, destPath);
            return;
        }

        if (!string.IsNullOrEmpty(_currentFolderPath))
            await NavigateToFolderAsync(_currentFolderPath).ConfigureAwait(true);
        SetTransientStatus("Renamed folder.");
    }

    internal bool TryBeginRenameFolderByPath(string folderFullPath)
    {
        if (string.IsNullOrEmpty(folderFullPath) || !Directory.Exists(folderFullPath))
            return false;

        var node = FindFolderTreeNodeByPath(FolderTree.RootNodes, folderFullPath);
        if (node?.Content is not FolderTreeEntry fe)
            return false;

        SyncBrowseTreeSelection(node);
        fe.EditingName = fe.DisplayLabel;
        fe.IsRenaming = true;
        _renameTargetNode = node;
        return true;
    }

    private void ScheduleFolderRenameMetricsAndResort(
        string destPath,
        TreeViewNode? renamedSubtreeRoot,
        IList<TreeViewNode>? forestForSubtreeMetrics)
    {
        var resortPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectIndexedAncestorPathsForResort(destPath, resortPaths);
        RequestCoalescedFolderResortForTouchedFolderPaths(resortPaths);

        var trimmedDest = destPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parentOfDest = Path.GetDirectoryName(trimmedDest);
        if (!string.IsNullOrEmpty(parentOfDest))
            EnqueueFolderMetricsRescan(parentOfDest, FolderMetricsScanScope.ImmediateChildren);

        IEnumerable<TreeViewNode> scanNodes;
        if (renamedSubtreeRoot != null)
            scanNodes = EnumerateNodesDepthFirst(renamedSubtreeRoot);
        else if (forestForSubtreeMetrics != null)
            scanNodes = EnumerateNodesDepthFirst(forestForSubtreeMetrics);
        else
            scanNodes = Enumerable.Empty<TreeViewNode>();

        foreach (var n in scanNodes)
        {
            if (n.Content is not FolderTreeEntry fe)
                continue;
            var scope = n.IsExpanded ? FolderMetricsScanScope.FullSubtree : FolderMetricsScanScope.ImmediateChildren;
            EnqueueFolderMetricsRescan(fe.Path, scope);
        }
    }

    private void ApplyBrowseRootFolderRelocatedInPlace(string oldPath, string destPath)
    {
        var pathsToUnindex = EnumerateNodesDepthFirst(FolderTree.RootNodes)
            .Select(n => n.Content as FolderTreeEntry)
            .Where(f => f != null)
            .Select(f => f!.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var p in pathsToUnindex)
        {
            _folderTreeEntryByPath.Remove(p);
            UnregisterFolderTreeNodeIndex(p);
            _folderAggregateBytesByPath.Remove(p);
            _folderImageFileCountByPath.Remove(p);
        }

        _sortSession.RelocatePathsForDirectoryRename(oldPath, destPath);

        foreach (var n in EnumerateNodesDepthFirst(FolderTree.RootNodes))
        {
            if (n.Content is FolderTreeEntry fe)
            {
                var newP = RelocateFilesystemPathUnderRoot(fe.Path, oldPath, destPath);
                if (!string.Equals(fe.Path, newP, StringComparison.OrdinalIgnoreCase))
                {
                    fe.Path = newP;
                    fe.DisplayLabel = new DirectoryInfo(newP).Name;
                }
            }
            else if (n.Content is ImageRow row)
            {
                var newP = RelocateFilesystemPathUnderRoot(row.FullPath, oldPath, destPath);
                if (string.Equals(row.FullPath, newP, StringComparison.OrdinalIgnoreCase))
                    continue;
                var fi = new FileInfo(newP);
                row.ApplyRenamedPath(newP, fi.Name, fi.Length, new DateTimeOffset(fi.LastWriteTimeUtc));
            }
        }

        foreach (var n in EnumerateNodesDepthFirst(FolderTree.RootNodes))
        {
            if (n.Content is FolderTreeEntry fe2)
                RegisterFolderTreeIndex(fe2, n);
        }

        RelocateAppPathsAfterFolderRename(oldPath, destPath);
        RefreshAllSortFlagDisplaysInList();
        UpdatePathOverlays();
        if (!string.IsNullOrEmpty(_currentImageFullPath))
            EnqueuePreviewNavigation(_currentImageFullPath, false);
        PersistLayout();
        SetTransientStatus("Renamed folder.");
        ScheduleFolderRenameMetricsAndResort(destPath, renamedSubtreeRoot: null, FolderTree.RootNodes);
    }

    private void ApplyRenamedFolderSubtreeInPlace(
        TreeViewNode renamedNode,
        string oldPath,
        string destPath,
        FolderTreeEntry rootEntry)
    {
        var pathsToUnindex = EnumerateNodesDepthFirst(renamedNode)
            .Select(n => n.Content as FolderTreeEntry)
            .Where(f => f != null)
            .Select(f => f!.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var p in pathsToUnindex)
        {
            _folderTreeEntryByPath.Remove(p);
            UnregisterFolderTreeNodeIndex(p);
            _folderAggregateBytesByPath.Remove(p);
            _folderImageFileCountByPath.Remove(p);
        }

        rootEntry.Path = destPath;
        rootEntry.DisplayLabel = new DirectoryInfo(destPath).Name;

        _sortSession.RelocatePathsForDirectoryRename(oldPath, destPath);

        foreach (var n in EnumerateNodesDepthFirst(renamedNode))
        {
            if (ReferenceEquals(n, renamedNode))
                continue;

            if (n.Content is FolderTreeEntry fe)
            {
                var newP = RelocateFilesystemPathUnderRoot(fe.Path, oldPath, destPath);
                if (!string.Equals(fe.Path, newP, StringComparison.OrdinalIgnoreCase))
                {
                    fe.Path = newP;
                    fe.DisplayLabel = new DirectoryInfo(newP).Name;
                }
            }
            else if (n.Content is ImageRow row)
            {
                var newP = RelocateFilesystemPathUnderRoot(row.FullPath, oldPath, destPath);
                if (string.Equals(row.FullPath, newP, StringComparison.OrdinalIgnoreCase))
                    continue;
                var fi = new FileInfo(newP);
                row.ApplyRenamedPath(newP, fi.Name, fi.Length, new DateTimeOffset(fi.LastWriteTimeUtc));
            }
        }

        foreach (var n in EnumerateNodesDepthFirst(renamedNode))
        {
            if (n.Content is FolderTreeEntry fe2)
                RegisterFolderTreeIndex(fe2, n);
        }

        RelocateAppPathsAfterFolderRename(oldPath, destPath);
        RefreshAllSortFlagDisplaysInList();
        UpdatePathOverlays();
        if (!string.IsNullOrEmpty(_currentImageFullPath))
            EnqueuePreviewNavigation(_currentImageFullPath, false);
        PersistLayout();
        SetTransientStatus("Renamed folder.");
        ScheduleFolderRenameMetricsAndResort(destPath, renamedNode, forestForSubtreeMetrics: null);
    }

    private static string RelocateFilesystemPathUnderRoot(string fullPath, string oldRoot, string newRoot)
    {
        try
        {
            var fr = Path.GetFullPath(fullPath);
            var or = Path.GetFullPath(oldRoot);
            var nr = Path.GetFullPath(newRoot);
            var sep = Path.DirectorySeparatorChar;
            if (string.Equals(fr, or, StringComparison.OrdinalIgnoreCase))
                return nr;
            if (fr.StartsWith(or + sep, StringComparison.OrdinalIgnoreCase))
                return nr + fr.Substring(or.Length);
        }
        catch
        {
            // ignored
        }

        return fullPath;
    }

    private void RelocateAppPathsAfterFolderRename(string oldRoot, string newRoot)
    {
        string? reloc(string? p)
        {
            if (string.IsNullOrEmpty(p))
                return p;
            var np = RelocateFilesystemPathUnderRoot(p, oldRoot, newRoot);
            return string.Equals(p, np, StringComparison.OrdinalIgnoreCase) ? p : np;
        }

        _currentFolderPath = reloc(_currentFolderPath);
        _currentImageFullPath = reloc(_currentImageFullPath);
        _browseNavAnchorPath = reloc(_browseNavAnchorPath);
        _pendingSelectImagePath = reloc(_pendingSelectImagePath);
        _session.LastBrowseFolder = reloc(_session.LastBrowseFolder);
        _session.LastSelectedImage = reloc(_session.LastSelectedImage);
        _deleteArchiveWizardCapturedWorkingFolder = reloc(_deleteArchiveWizardCapturedWorkingFolder);
    }

    /// <summary>Reference equality for WinRT sibling lists (WinUI exposes a non-generic <c>ReferenceEqualityComparer</c> that shadows BCL).</summary>
    private sealed class ResortSiblingListReferenceEqualityComparer : IEqualityComparer<IList<TreeViewNode>>
    {
        internal static readonly ResortSiblingListReferenceEqualityComparer Instance = new();

        private ResortSiblingListReferenceEqualityComparer()
        {
        }

        public bool Equals(IList<TreeViewNode>? x, IList<TreeViewNode>? y) => ReferenceEquals(x, y);

        public int GetHashCode(IList<TreeViewNode> obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
