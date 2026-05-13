using System.Collections.Concurrent;
using System.Collections.Generic;
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
    private readonly Dictionary<string, FolderTreeEntry> _folderTreeEntryByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long?> _folderAggregateBytesByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _folderMetricsInFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _folderMetricsConcurrency = new(2, 2);
    private CancellationTokenSource? _folderMetricsCts = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _folderResortCoalesceTimer;

    private TreeViewNode? _renameTargetNode;
    private bool _renameCommitInProgress;

    private TreeViewNode? _browserContextMenuTargetNode;
    private MenuFlyout? _browserTreeContextMenu;
    private bool _browserContextMenuIsToolbarCurrentFolder;

    private void WireBrowserTreeTemplates()
    {
        FolderTree.ItemTemplateSelector = new BrowserTreeItemTemplateSelector
        {
            FolderTemplate = (DataTemplate)RootGrid.Resources["BrowserFolderItemTemplate"],
            FileTemplate = (DataTemplate)RootGrid.Resources["BrowserFileItemTemplate"],
            HeaderTemplate = (DataTemplate)RootGrid.Resources["BrowserFileListHeaderTemplate"],
            FolderHeaderTemplate = (DataTemplate)RootGrid.Resources["BrowserFolderListHeaderTemplate"],
        };
        FolderTree.DoubleTapped += FolderTree_DoubleTapped;
        FolderTree.PreviewKeyDown += FolderTree_PreviewKeyDown;

        _browserTreeContextMenu = new MenuFlyout();
        var renameItem = new MenuFlyoutItem { Text = "Rename" };
        renameItem.Click += (_, _) => BrowserContextRename_Click();
        _browserTreeContextMenu.Items.Add(renameItem);
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
        if (node?.Content is BrowserFileListHeaderMarker or BrowserFolderListHeaderMarker)
            return;
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
        FolderTree.SelectedNode = _browserContextMenuTargetNode;
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

    private void RegisterFolderTreeIndex(FolderTreeEntry entry)
    {
        _folderTreeEntryByPath[entry.Path] = entry;
        var agg = entry.AggregateSizeBytes;
        if (agg != null)
            _folderAggregateBytesByPath[entry.Path] = agg;
    }

    private void ResetBrowserFolderMetricsState()
    {
        _folderTreeEntryByPath.Clear();
        _folderAggregateBytesByPath.Clear();
        _folderMetricsInFlight.Clear();
        _folderMetricsCts?.Cancel();
        _folderMetricsCts?.Dispose();
        _folderMetricsCts = new CancellationTokenSource();
    }

    private void AppendBrowserFolderAndImageNodes(
        IList<TreeViewNode> target,
        IEnumerable<FileSystemEntry> dirEntries,
        IReadOnlyList<ImageRow> rows)
    {
        var dirs = FolderDirectorySort.SortDirectories(dirEntries, _layoutState.FolderListSort, _folderAggregateBytesByPath)
            .ToList();

        if (_layoutState.ShowBrowserFolderColumnHeadings && dirs.Count > 0)
        {
            var fh = new BrowserFolderListHeaderMarker();
            ApplyLayoutFolderDetailsToFolderHeaderMarker(fh);
            target.Add(new TreeViewNode
            {
                Content = fh,
                HasUnrealizedChildren = false,
            });
        }

        foreach (var d in dirs)
        {
            var entry = FolderTreeEntry.FromDirectoryEntry(d);
            PrepareFolderEntrySizingState(entry);
            ApplyLayoutFolderDetailsToFolderEntry(entry);
            RegisterFolderTreeIndex(entry);
            var node = new TreeViewNode
            {
                Content = entry,
            };
            node.HasUnrealizedChildren = DirHasExpandableChildren(d.FullPath);
            target.Add(node);
            EnqueueFolderMetricsScanIfNeeded(entry.Path, FolderMetricsScanScope.ImmediateChildren);
        }

        if (_layoutState.ShowBrowserFileColumnHeadings && rows.Count > 0)
        {
            var marker = new BrowserFileListHeaderMarker();
            ApplyLayoutFileDetailsToHeaderMarker(marker);
            target.Add(new TreeViewNode
            {
                Content = marker,
                HasUnrealizedChildren = false,
            });
        }

        foreach (var r in rows)
        {
            target.Add(new TreeViewNode
            {
                Content = r,
                HasUnrealizedChildren = false,
            });
        }
    }

    private void SyncBrowserFolderListHeaderNodes()
    {
        var lists = new List<IList<TreeViewNode>> { FolderTree.RootNodes };
        foreach (var n in EnumerateNodesDepthFirst(FolderTree.RootNodes))
        {
            if (n.Children.Count > 0)
                lists.Add(n.Children);
        }

        foreach (var list in lists)
            SyncBrowserFolderHeaderRowInChildren(list);
    }

    private void SyncBrowserFolderHeaderRowInChildren(IList<TreeViewNode> children)
    {
        for (var i = children.Count - 1; i >= 0; i--)
        {
            if (children[i].Content is BrowserFolderListHeaderMarker)
                children.RemoveAt(i);
        }

        if (!_layoutState.ShowBrowserFolderColumnHeadings)
            return;

        var firstFolder = -1;
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i].Content is FolderTreeEntry)
            {
                firstFolder = i;
                break;
            }
        }

        if (firstFolder < 0)
            return;

        var marker = new BrowserFolderListHeaderMarker();
        ApplyLayoutFolderDetailsToFolderHeaderMarker(marker);
        children.Insert(
            firstFolder,
            new TreeViewNode
            {
                Content = marker,
                HasUnrealizedChildren = false,
            });
    }

    private void ApplyBrowserFolderDetailsChrome()
    {
        foreach (var n in EnumerateNodesDepthFirst(FolderTree.RootNodes))
        {
            switch (n.Content)
            {
                case BrowserFolderListHeaderMarker m:
                    ApplyLayoutFolderDetailsToFolderHeaderMarker(m);
                    break;
                case FolderTreeEntry row:
                    ApplyLayoutFolderDetailsToFolderEntry(row);
                    break;
            }
        }
    }

    private void ResortAllFolderGroups()
    {
        var lists = new List<IList<TreeViewNode>> { FolderTree.RootNodes };
        foreach (var n in EnumerateNodesDepthFirst(FolderTree.RootNodes))
        {
            if (n.Children.Count > 0)
                lists.Add(n.Children);
        }

        foreach (var list in lists)
            ResortFolderSiblingBlock(list);
    }

    private void ResortFolderSiblingBlock(IList<TreeViewNode> children)
    {
        TreeViewNode? folderHeader = null;
        var folderNodes = new List<TreeViewNode>();
        TreeViewNode? fileHeader = null;
        var imageNodes = new List<TreeViewNode>();
        foreach (var n in children.ToList())
        {
            switch (n.Content)
            {
                case BrowserFolderListHeaderMarker:
                    folderHeader = n;
                    break;
                case FolderTreeEntry:
                    folderNodes.Add(n);
                    break;
                case BrowserFileListHeaderMarker:
                    fileHeader = n;
                    break;
                case ImageRow:
                    imageNodes.Add(n);
                    break;
            }
        }

        if (folderNodes.Count == 0)
            return;

        folderNodes.Sort((x, y) => CompareFolderTreeNodes(x, y, _layoutState.FolderListSort));

        children.Clear();
        if (folderHeader != null)
            children.Add(folderHeader);
        foreach (var fn in folderNodes)
            children.Add(fn);
        if (fileHeader != null)
            children.Add(fileHeader);
        foreach (var im in imageNodes)
            children.Add(im);
    }

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

    private static string FolderMetricsFlightKey(string path, FolderMetricsScanScope scope) =>
        path + "\u001E" + (int)scope;

    private void EnqueueFolderMetricsScanIfNeeded(string path, FolderMetricsScanScope scope)
    {
        if (!_layoutState.CalculateFolderSizesInBackground
            || (!_layoutState.ShowBrowserFolderSize && !_layoutState.ShowBrowserFolderImageCount))
            return;
        _ = StartFolderMetricsWorkAsync(path, scope);
    }

    private async Task StartFolderMetricsWorkAsync(string path, FolderMetricsScanScope scope)
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

            if (cached != null && gen == Volatile.Read(ref _populateBrowserGeneration))
            {
                _ = DispatcherQueue.TryEnqueue(() => ApplyFolderMetricsSnapshot(path, cached, gen));
            }

            if (gen != Volatile.Read(ref _populateBrowserGeneration))
                return;

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

            _ = DispatcherQueue.TryEnqueue(() => ApplyFolderMetricsSnapshot(path, snap, gen));
        }
        finally
        {
            _folderMetricsConcurrency.Release();
            _folderMetricsInFlight.TryRemove(flightKey, out _);
        }
    }

    private void ApplyFolderMetricsSnapshot(string path, FolderMetricsSnapshot snap, int gen)
    {
        if (gen != Volatile.Read(ref _populateBrowserGeneration))
            return;
        if (!_folderTreeEntryByPath.TryGetValue(path, out var fe))
            return;
        fe.SetAggregateSize(snap.AggregateSizeBytes);
        fe.SetImageFileCount(snap.ImageFileCount);
        _folderAggregateBytesByPath[path] = snap.AggregateSizeBytes;
        RequestCoalescedFolderResortIfSortedBySize();
    }

    private void RequestCoalescedFolderResortIfSortedBySize()
    {
        if (_layoutState.FolderListSort != FolderListSortKind.AggregateSize)
            return;
        var dq = DispatcherQueue;
        _folderResortCoalesceTimer ??= dq.CreateTimer();
        _folderResortCoalesceTimer.Interval = TimeSpan.FromMilliseconds(300);
        _folderResortCoalesceTimer.IsRepeating = false;
        _folderResortCoalesceTimer.Tick -= OnFolderResortCoalesceTick;
        _folderResortCoalesceTimer.Tick += OnFolderResortCoalesceTick;
        _folderResortCoalesceTimer.Stop();
        _folderResortCoalesceTimer.Start();
    }

    private void OnFolderResortCoalesceTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        sender.Tick -= OnFolderResortCoalesceTick;
        ResortAllFolderGroups();
        SyncBrowserFolderListHeaderNodes();
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

    private void SyncBrowserFileListHeaderNodes()
    {
        var lists = new List<IList<TreeViewNode>> { FolderTree.RootNodes };
        foreach (var n in EnumerateNodesDepthFirst(FolderTree.RootNodes))
        {
            if (n.Children.Count > 0)
                lists.Add(n.Children);
        }

        foreach (var list in lists)
            SyncBrowserListHeaderRowInChildren(list);
    }

    private void SyncBrowserListHeaderRowInChildren(IList<TreeViewNode> children)
    {
        for (var i = children.Count - 1; i >= 0; i--)
        {
            if (children[i].Content is BrowserFileListHeaderMarker)
                children.RemoveAt(i);
        }

        if (!_layoutState.ShowBrowserFileColumnHeadings)
            return;

        var firstImage = -1;
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i].Content is ImageRow)
            {
                firstImage = i;
                break;
            }
        }

        if (firstImage < 0)
            return;

        var marker = new BrowserFileListHeaderMarker();
        ApplyLayoutFileDetailsToHeaderMarker(marker);
        children.Insert(
            firstImage,
            new TreeViewNode
            {
                Content = marker,
                HasUnrealizedChildren = false,
            });
    }

    private void ApplyBrowserFileDetailsChrome()
    {
        foreach (var n in EnumerateNodesDepthFirst(FolderTree.RootNodes))
        {
            switch (n.Content)
            {
                case BrowserFileListHeaderMarker m:
                    ApplyLayoutFileDetailsToHeaderMarker(m);
                    break;
                case ImageRow row:
                    ApplyLayoutFileDetailsToImageRow(row);
                    break;
            }
        }
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
        ResetBrowserFolderMetricsState();
        _currentFolderPath = path;
        _session.LastBrowseFolder = path;
        _browseNavAnchorPath = null;
        FolderTree.RootNodes.Clear();
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
            entries = await AppServices.FileSystem.ListDirectoryAsync(path).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (gen != Volatile.Read(ref _populateBrowserGeneration))
                return;
            SetTransientStatus(ex.Message);
            return;
        }

        if (gen != Volatile.Read(ref _populateBrowserGeneration))
            return;

        var dirs = FolderDirectorySort.SortDirectories(entries.Where(x => x.IsDirectory), _layoutState.FolderListSort, _folderAggregateBytesByPath)
            .ToList();

        var imageEntries = entries
            .Where(x => !x.IsDirectory && ImageExtensions.IsImageFile(x.FullPath))
            .ToList();

        var rows = new List<ImageRow>(imageEntries.Count);
        foreach (var e in imageEntries)
            rows.Add(CreateImageRowFromEntry(e));
        rows = ApplyListSort(rows).ToList();

        AppendBrowserFolderAndImageNodes(FolderTree.RootNodes, dirs, rows);

        var flatEntryCount = entries.Count;
        var status = rows.Count == 0 && flatEntryCount > 0
            ? $"0 image(s) · {flatEntryCount} item(s) in folder (none match supported raster extensions)"
            : $"{rows.Count} image(s) · {dirs.Count} folder(s)";
        SetTransientStatus(status);

        var selectPath = _pendingSelectImagePath;
        _pendingSelectImagePath = null;

        TreeViewNode? selectNode = null;
        if (!string.IsNullOrEmpty(selectPath))
            selectNode = FindImageNodeByPath(FolderTree.RootNodes, selectPath);
        if (selectNode == null && rows.Count > 0)
            selectNode = FirstImageNodePreorder(FolderTree.RootNodes);

        if (selectNode != null)
            FolderTree.SelectedNode = selectNode;
        else if (rows.Count == 0)
            ClearImageSelectionAndPreviewCore();

        UpdatePathOverlays();
        UpdateFullscreenMenuEnabled();
        PersistLayout();
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

        IReadOnlyList<FileSystemEntry> entries;
        try
        {
            entries = await AppServices.FileSystem.ListDirectoryAsync(path).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetTransientStatus(ex.Message);
            return;
        }

        if (populateGen is { } g && g != Volatile.Read(ref _populateBrowserGeneration))
            return;

        var dirs = FolderDirectorySort.SortDirectories(entries.Where(x => x.IsDirectory), _layoutState.FolderListSort, _folderAggregateBytesByPath)
            .ToList();

        var imageEntries = entries
            .Where(x => !x.IsDirectory && ImageExtensions.IsImageFile(x.FullPath))
            .ToList();

        var rows = new List<ImageRow>(imageEntries.Count);
        foreach (var e in imageEntries)
            rows.Add(CreateImageRowFromEntry(e));
        rows = ApplyListSort(rows).ToList();

        AppendBrowserFolderAndImageNodes(node.Children, dirs, rows);

        node.HasUnrealizedChildren = false;
    }

    private async void FolderTree_OnExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (_treeExpansionBusy)
            return;
        var node = args.Node;
        var path = GetFolderPath(node);
        if (node.Children.Count > 0)
        {
            node.HasUnrealizedChildren = false;
            if (!string.IsNullOrEmpty(path))
                EnqueueFolderMetricsScanIfNeeded(path, FolderMetricsScanScope.FullSubtree);
            return;
        }

        if (string.IsNullOrEmpty(path))
            return;

        _treeExpansionBusy = true;
        try
        {
            await PopulateFolderTreeNodeChildrenAsync(node, path, populateGen: null).ConfigureAwait(true);
        }
        finally
        {
            _treeExpansionBusy = false;
        }

        EnqueueFolderMetricsScanIfNeeded(path, FolderMetricsScanScope.FullSubtree);
    }

    private async void FolderTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (sender.SelectedNode?.Content is ImageRow row)
        {
            if (_suppressTreeSelectionPreviewEnqueue)
            {
                _browseNavAnchorPath = row.FullPath;
                _session.LastSelectedImage = row.FullPath;
                UpdateFullscreenMenuEnabled();
                return;
            }

            _browseNavAnchorPath = row.FullPath;
            await OnFolderTreeImageRowSelectedAsync(row).ConfigureAwait(true);
            UpdateFullscreenMenuEnabled();
            return;
        }

        _browseNavAnchorPath = null;
        UpdateFullscreenMenuEnabled();
    }

    /// <summary>Clears browser image selection and blanks preview; no-op in fullscreen (caller should guard).</summary>
    private void ClearImageSelectionAndPreview()
    {
        if (_isFullscreen)
            return;
        ClearImageSelectionAndPreviewCore();
    }

    /// <summary>Blanks preview and clears tree selection; used when the current file is no longer visible in the tree (e.g. folder collapsed) regardless of fullscreen.</summary>
    private void ClearImageSelectionAndPreviewCore()
    {
        if (_renameTargetNode != null)
            CancelInlineRename(commit: false);

        StopSlideshowSession();

        FolderTree.SelectedNode = null;
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
    }

    private void FolderTree_OnCollapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        if (args.Node?.Content is FolderTreeEntry feCollapsed)
            EnqueueFolderMetricsScanIfNeeded(feCollapsed.Path, FolderMetricsScanScope.ImmediateChildren);

        if (_suppressFolderTreeCollapsedClear)
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
            ClearImageSelectionAndPreviewCore();
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
        FolderTree.SelectedNode?.Content as ImageRow;

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

    private List<TreeViewNode> CollectImageNodesForBrowseContextDirectory(string contextDir)
    {
        var list = new List<TreeViewNode>();
        if (string.IsNullOrEmpty(_currentFolderPath))
            return list;

        if (SameDirectoryPath(contextDir, _currentFolderPath))
        {
            foreach (var n in FolderTree.RootNodes)
            {
                if (n.Content is ImageRow)
                    list.Add(n);
            }

            return list;
        }

        var folderNode = FindFolderTreeNodeByPath(FolderTree.RootNodes, contextDir);
        if (folderNode == null)
            return list;

        foreach (var c in folderNode.Children)
        {
            if (c.Content is ImageRow)
                list.Add(c);
        }

        return list;
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

    private bool ApplyOverlayListPositionFromTree()
    {
        void hideBoth()
        {
            NormalPathPositionText.Visibility = Visibility.Collapsed;
            FullscreenPathPositionText.Visibility = Visibility.Collapsed;
        }

        if (!_layoutState.ShowOverlayListPosition)
        {
            hideBoth();
            return false;
        }

        var path = _currentImageFullPath;
        if (string.IsNullOrEmpty(path))
        {
            hideBoth();
            return false;
        }

        var sel = FolderTree.SelectedNode;
        var selPath = sel?.Content is ImageRow sr ? sr.FullPath : null;
        var contextDir = BrowseContextDirectory.Resolve(_browseNavAnchorPath, selPath, path, _currentFolderPath);
        if (string.IsNullOrEmpty(contextDir))
        {
            hideBoth();
            return false;
        }

        var fullNodes = CollectImageNodesForBrowseContextDirectory(contextDir);
        var (nodes, _) = BuildFilteredBrowseNavNodesAndPaths(fullNodes);
        if (nodes.Count == 0)
        {
            hideBoth();
            return false;
        }

        var index = -1;
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].Content is not ImageRow r)
                continue;
            if (!string.Equals(r.FullPath, path, StringComparison.OrdinalIgnoreCase))
                continue;
            index = i;
            break;
        }

        if (index < 0)
        {
            hideBoth();
            return false;
        }

        var text = $"{index + 1}/{nodes.Count}";
        NormalPathPositionText.Text = text;
        FullscreenPathPositionText.Text = text;
        NormalPathPositionText.Visibility = Visibility.Visible;
        FullscreenPathPositionText.Visibility = Visibility.Visible;
        return true;
    }

    private bool TryResolveBrowseNavigationTarget(
        BrowseNavStepKind step,
        out string path,
        out TreeViewNode? node)
    {
        path = "";
        node = null;
        var sel = FolderTree.SelectedNode;
        var selPath = sel?.Content is ImageRow sr ? sr.FullPath : null;
        var contextDir = BrowseContextDirectory.Resolve(_browseNavAnchorPath, selPath, _currentImageFullPath, _currentFolderPath);
        if (string.IsNullOrEmpty(contextDir))
            return false;

        var fullNodes = CollectImageNodesForBrowseContextDirectory(contextDir);
        var (nodes, paths) = BuildFilteredBrowseNavNodesAndPaths(fullNodes);
        if (nodes.Count == 0)
            return false;

        var i = BrowseSequentialNavIndex.ComputeTargetIndexForStep(
            paths,
            _browseNavAnchorPath,
            selPath,
            _currentImageFullPath,
            step);
        if (i < 0)
            return false;

        _browseNavAnchorPath = paths[i];
        path = paths[i];
        node = nodes[i];
        return true;
    }

    /// <summary>Begin sibling-folder navigation (parent of image context directory, same sort as tree). Does not await.</summary>
    internal void BrowseNavigateSiblingFolderFromInput(int delta)
    {
        _ = BrowseNavigateSiblingFolderAsync(delta);
    }

    private async Task BrowseNavigateSiblingFolderAsync(int delta)
    {
        if (delta != 1 && delta != -1)
            return;

        var sel = FolderTree.SelectedNode;
        var selPath = sel?.Content is ImageRow sr ? sr.FullPath : null;
        var contextDir = BrowseContextDirectory.Resolve(
            _browseNavAnchorPath,
            selPath,
            _currentImageFullPath,
            _currentFolderPath);
        if (string.IsNullOrEmpty(contextDir))
            return;

        var parent = Directory.GetParent(contextDir);
        if (parent == null)
            return;

        var gen = Volatile.Read(ref _populateBrowserGeneration);
        IReadOnlyList<FileSystemEntry> entries;
        try
        {
            entries = await AppServices.FileSystem.ListDirectoryAsync(parent.FullName).ConfigureAwait(true);
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
            _folderAggregateBytesByPath);

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

        if (!string.IsNullOrEmpty(_currentFolderPath) && SameDirectoryPath(contextDir, _currentFolderPath))
        {
            if (gen == Volatile.Read(ref _populateBrowserGeneration))
                SetTransientStatus("Sibling folders are outside the current folder tree.");
            return;
        }

        var contextNode = FindFolderTreeNodeByPath(FolderTree.RootNodes, contextDir);
        if (contextNode == null)
            return;

        var siblingNode = FindFolderSiblingTreeNode(contextNode, targetFolderPath);
        if (siblingNode == null)
            return;

        _treeExpansionBusy = true;
        try
        {
            var folderPath = GetFolderPath(siblingNode);
            if (string.IsNullOrEmpty(folderPath))
                return;

            await PopulateFolderTreeNodeChildrenAsync(siblingNode, folderPath, gen).ConfigureAwait(true);
        }
        finally
        {
            _treeExpansionBusy = false;
        }

        if (gen != Volatile.Read(ref _populateBrowserGeneration))
            return;

        siblingNode.IsExpanded = true;

        var firstImage = FirstImageNodePreorderMatchingNavMode(new List<TreeViewNode> { siblingNode });
        if (firstImage?.Content is not ImageRow row)
            ClearImageSelectionAndPreviewCore();
        else
        {
            EnqueuePreviewNavigation(row.FullPath, false);
            SyncBrowseTreeSelection(firstImage);
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

    /// <summary>
    /// Keyboard/command browse step: resolve target from visible list, enqueue preview immediately, then sync tree under suppress.
    /// </summary>
    private void BrowseNavigateByStep(BrowseNavStepKind step)
    {
        IncrementNavCommandCounter();
        if (!TryResolveBrowseNavigationTarget(step, out var path, out var node) || node == null)
            return;

        EnqueuePreviewNavigation(path, false);
        SyncBrowseTreeSelection(node);
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

    private TreeViewNode? FirstImageNodePreorderMatchingNavMode(List<TreeViewNode> roots)
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
        if (_browseNavigationMode == BrowseNavigationMode.AllImages)
            return;
        var path = _currentImageFullPath;
        if (string.IsNullOrEmpty(path))
            return;
        if (BrowseNavigationModeFilter.Matches(_sortSession.GetState(path), _browseNavigationMode))
            return;
        if (!TryResolveBrowseNavigationTarget(BrowseNavStepKind.First, out var firstPath, out var firstNode) || firstNode == null)
        {
            ClearImageSelectionAndPreview();
            return;
        }

        EnqueuePreviewNavigation(firstPath, false);
        SyncBrowseTreeSelection(firstNode);
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

        if (TryDispatchInputCommand(e))
            return;

        if (e.Key == VirtualKey.Enter && FolderTree.SelectedNode?.Content is ImageRow)
        {
            TryEnterFullscreen();
            e.Handled = true;
        }
    }

    private bool TryBeginRenameSelectedBrowserItem()
    {
        var node = FolderTree.SelectedNode;
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
        if (!commit && _renameTargetNode?.Content is FolderTreeEntry)
            _skipNavigateAfterFolderCommit = false;

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

        string destPath;
        try
        {
            destPath = BrowserPaneRenameHelper.PickUniqueFileName(dir, desired);
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

        string destPath;
        try
        {
            destPath = BrowserPaneRenameHelper.PickUniqueDirectoryName(parent, trimmed);
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

        var renamedNode = _renameTargetNode;
        folderEntry.IsRenaming = false;
        _renameTargetNode = null;

        if (!string.IsNullOrEmpty(_currentFolderPath)
            && string.Equals(_currentFolderPath, oldPath, StringComparison.OrdinalIgnoreCase))
        {
            _currentFolderPath = destPath;
            _session.LastBrowseFolder = destPath;
        }

        var skipNavigate = _skipNavigateAfterFolderCommit;
        _skipNavigateAfterFolderCommit = false;

        if (skipNavigate && renamedNode != null)
        {
            ApplyRenamedFolderSubtreeInPlace(renamedNode, oldPath, destPath, folderEntry);
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

        FolderTree.SelectedNode = node;
        fe.EditingName = fe.DisplayLabel;
        fe.IsRenaming = true;
        _renameTargetNode = node;
        return true;
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
            _folderAggregateBytesByPath.Remove(p);
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
                RegisterFolderTreeIndex(fe2);
        }

        RelocateAppPathsAfterFolderRename(oldPath, destPath);
        RefreshAllSortFlagDisplaysInList();
        UpdatePathOverlays();
        if (!string.IsNullOrEmpty(_currentImageFullPath))
            EnqueuePreviewNavigation(_currentImageFullPath, false);
        PersistLayout();
        SetTransientStatus("Renamed folder.");
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
}
