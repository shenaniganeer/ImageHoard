using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageHoard.Core;
using ImageHoard.Core.Browse;
using ImageHoard.Core.Models;
using ImageHoard.Core.Services;
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
        if (node?.Content is BrowserFileListHeaderMarker)
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

    private void AppendBrowserFolderAndImageNodes(
        IList<TreeViewNode> target,
        IEnumerable<FileSystemEntry> dirEntries,
        IReadOnlyList<ImageRow> rows)
    {
        foreach (var d in dirEntries)
        {
            var node = new TreeViewNode
            {
                Content = new FolderTreeEntry(d.FullPath, d.Name),
            };
            node.HasUnrealizedChildren = DirHasExpandableChildren(d.FullPath);
            target.Add(node);
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

        var dirs = entries
            .Where(x => x.IsDirectory)
            .OrderBy(x => x.Name, NaturalStringComparer.OrdinalIgnoreCase)
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
        if (selectNode == null && !_layoutState.ShowBrowserPane && rows.Count > 0)
            selectNode = FirstImageNodePreorder(FolderTree.RootNodes);

        if (selectNode != null)
            FolderTree.SelectedNode = selectNode;

        UpdatePathOverlays();
        UpdateFullscreenMenuEnabled();
        PersistLayout();
    }

    private static bool DirHasExpandableChildren(string dirPath)
    {
        try
        {
            if (Directory.EnumerateDirectories(dirPath).Take(1).Any())
                return true;
            foreach (var f in Directory.EnumerateFiles(dirPath))
            {
                if (ImageExtensions.IsImageFile(f))
                    return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    internal async Task RefreshBrowserTreeFromSettingsAsync()
    {
        if (string.IsNullOrEmpty(_currentFolderPath))
            return;
        if (!string.IsNullOrEmpty(_currentImageFullPath))
            _pendingSelectImagePath = _currentImageFullPath;
        await NavigateToFolderAsync(_currentFolderPath).ConfigureAwait(true);
    }

    private async void FolderTree_OnExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (_treeExpansionBusy)
            return;
        var node = args.Node;
        if (node.Children.Count > 0)
        {
            node.HasUnrealizedChildren = false;
            return;
        }

        var path = GetFolderPath(node);
        if (string.IsNullOrEmpty(path))
            return;

        _treeExpansionBusy = true;
        try
        {
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

            var dirs = entries
                .Where(x => x.IsDirectory)
                .OrderBy(x => x.Name, NaturalStringComparer.OrdinalIgnoreCase)
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
        finally
        {
            _treeExpansionBusy = false;
        }
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

        if (_renameTargetNode != null)
            CancelInlineRename(commit: false);

        if (_slideshowUiActive)
        {
            _slideshowUiActive = false;
            _slideshow?.Tree.StopEnumeration();
            _slideshow = null;
            UpdateSlideshowScopeBadge();
        }

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

        var nodes = CollectVisibleImageNodesPreorder(FolderTree.RootNodes);
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
        var nodes = CollectVisibleImageNodesPreorder(FolderTree.RootNodes);
        if (nodes.Count == 0)
            return false;

        var paths = BuildVisibleImagePathsFromNodes(nodes);
        var sel = FolderTree.SelectedNode;
        var selPath = sel?.Content is ImageRow sr ? sr.FullPath : null;
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

        folderEntry.IsRenaming = false;
        _renameTargetNode = null;

        if (!string.IsNullOrEmpty(_currentFolderPath)
            && string.Equals(_currentFolderPath, oldPath, StringComparison.OrdinalIgnoreCase))
        {
            _currentFolderPath = destPath;
            _session.LastBrowseFolder = destPath;
        }

        if (!string.IsNullOrEmpty(_currentFolderPath))
            await NavigateToFolderAsync(_currentFolderPath).ConfigureAwait(true);
        SetTransientStatus("Renamed folder.");
    }
}
