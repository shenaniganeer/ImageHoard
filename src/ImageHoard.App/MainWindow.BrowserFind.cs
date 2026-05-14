using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageHoard.Core.Browse;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ImageHoard.App;

internal enum BrowserFindMatchKind
{
    Folder,
    File,
}

internal readonly record struct BrowserFindMatch(string Path, string DisplayName, BrowserFindMatchKind Kind);

internal enum BrowserFindSearchAnchor
{
    First,
    Last,
}

public sealed partial class MainWindow
{
    private readonly List<BrowserFindMatch> _browserFindMatches = new();
    private int _browserFindCurrentIndex;
    private CancellationTokenSource? _browserFindSearchCts;
    /// <summary>When non-null, <see cref="_browserFindMatches"/> was produced for these parameters (trimmed query).</summary>
    private BrowserFindSearchParameters? _browserFindMatchesForParameters;

    internal bool IsBrowserFindOverlayOpen =>
        BrowserFindOverlayRoot.Visibility == Visibility.Visible;

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

    /// <summary>Clears cached find matches and cancels an in-flight deep search (e.g. when match/target/deep options change).</summary>
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
            // ignore
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
        var sig = new BrowserFindSearchParameters(query.Trim(), matchFromStartOfName, foldersOnly, deepSearch);
        if (_browserFindMatches.Count > 0
            && (!_browserFindMatchesForParameters.HasValue || _browserFindMatchesForParameters.Value != sig))
        {
            ClearBrowserFindMatchCacheCore();
        }

        if (_browserFindMatches.Count == 0)
        {
            var anchor = delta > 0 ? BrowserFindSearchAnchor.First : BrowserFindSearchAnchor.Last;
            await RunBrowserFindSearchFromPanelAsync(
                    query,
                    matchFromStartOfName,
                    foldersOnly,
                    deepSearch,
                    anchor)
                .ConfigureAwait(true);
            return;
        }

        await BrowserFindStepMatchAsync(delta).ConfigureAwait(true);
    }

    internal async Task BrowserFindStepMatchAsync(int delta)
    {
        var n = _browserFindMatches.Count;
        if (n == 0)
            return;

        _browserFindCurrentIndex = ((_browserFindCurrentIndex + delta) % n + n) % n;
        var cur = _browserFindMatches[_browserFindCurrentIndex];
        await BrowserFindApplyMatchAsync(cur).ConfigureAwait(true);
        BrowserFindPanelElement.SetStatus($"{_browserFindCurrentIndex + 1} of {n}");
    }

    private async Task BrowserFindApplyMatchAsync(BrowserFindMatch m)
    {
        if (string.IsNullOrEmpty(_currentFolderPath))
            return;

        if (m.Kind == BrowserFindMatchKind.Folder)
        {
            if (!IsSameOrDescendantDirectory(_currentFolderPath, m.Path))
                return;
            if (!await TryEnsureFolderPathMaterializedAsync(
                    m.Path,
                    null,
                    expandAndPopulateDestinationFolder: false).ConfigureAwait(true))
                return;
            var node = TryResolveFolderTreeNodeForPath(m.Path)
                ?? FindFolderTreeNodeByPath(FolderTree.RootNodes, m.Path);
            if (node == null)
                return;
            SyncBrowseTreeSelection(node);
            FolderTree.UpdateLayout();
            var sel = FolderTree.SelectedNode ?? node;
            TryBringFolderTreeNodeToTop(sel);
            await ScheduleBrowserTreeViewportAfterMutationAsync(
                    m.Path,
                    preferTreeSelectionBeforeBrowsedFolder: true)
                .ConfigureAwait(true);
            return;
        }

        if (!IsSameOrDescendantDirectory(_currentFolderPath, m.Path))
            return;

        EnqueuePreviewNavigation(m.Path, false);
        await TrySyncBrowseTreeSelectionToImagePathAsync(m.Path).ConfigureAwait(true);
        var parentDir = Path.GetDirectoryName(m.Path);
        FolderTree.UpdateLayout();
        var imageSel = FolderTree.SelectedNode ?? FindImageNodeByPath(FolderTree.RootNodes, m.Path);
        if (imageSel != null)
            TryBringFolderTreeNodeToTop(imageSel);
        await ScheduleBrowserTreeViewportAfterMutationAsync(
                string.IsNullOrEmpty(parentDir) ? null : parentDir,
                preferTreeSelectionBeforeBrowsedFolder: true)
            .ConfigureAwait(true);
    }

    internal async Task RunBrowserFindSearchFromPanelAsync(
        string query,
        bool matchFromStartOfName,
        bool foldersOnly,
        bool deepSearch,
        BrowserFindSearchAnchor anchor = BrowserFindSearchAnchor.First)
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
            IReadOnlyList<BrowserFindMatch> found;
            if (deepSearch)
            {
                var root = _currentFolderPath;
                found = await Task.Run(
                        () => BrowserFindCollectDeep(root, trimmed, matchFromStartOfName, foldersOnly, ct),
                        ct)
                    .ConfigureAwait(true);
            }
            else
            {
                found = BrowserFindCollectShallow(trimmed, matchFromStartOfName, foldersOnly);
            }

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
            await BrowserFindApplyMatchAsync(cur).ConfigureAwait(true);
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

    private List<BrowserFindMatch> BrowserFindCollectShallow(
        string trimmed,
        bool matchFromStartOfName,
        bool foldersOnly)
    {
        var list = new List<BrowserFindMatch>();
        if (foldersOnly)
        {
            foreach (var n in EnumerateVisibleFolderTreeNodesPreorder(FolderTree.RootNodes))
            {
                if (n.Content is not FolderTreeEntry fe)
                    continue;
                if (!BrowserFindNameMatching.NameMatches(trimmed, fe.DisplayLabel, matchFromStartOfName))
                    continue;
                list.Add(new BrowserFindMatch(fe.Path, fe.DisplayLabel, BrowserFindMatchKind.Folder));
            }
        }
        else
        {
            foreach (var n in CollectVisibleImageNodesPreorder(FolderTree.RootNodes))
            {
                if (n.Content is not ImageRow ir)
                    continue;
                if (!BrowserFindNameMatching.NameMatches(trimmed, ir.DisplayName, matchFromStartOfName))
                    continue;
                list.Add(new BrowserFindMatch(ir.FullPath, ir.DisplayName, BrowserFindMatchKind.File));
            }
        }

        return list;
    }

    private static List<BrowserFindMatch> BrowserFindCollectDeep(
        string root,
        string trimmed,
        bool matchFromStartOfName,
        bool foldersOnly,
        CancellationToken ct)
    {
        var list = new List<BrowserFindMatch>();
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
        };

        if (foldersOnly)
        {
            foreach (var dir in Directory.EnumerateDirectories(root, "*", opts))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name))
                    continue;
                if (!BrowserFindNameMatching.NameMatches(trimmed, name, matchFromStartOfName))
                    continue;
                list.Add(new BrowserFindMatch(dir, name, BrowserFindMatchKind.Folder));
            }
        }
        else
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", opts))
            {
                ct.ThrowIfCancellationRequested();
                if (!ImageExtensions.IsImageFile(file))
                    continue;
                var name = Path.GetFileName(file);
                if (string.IsNullOrEmpty(name))
                    continue;
                if (!BrowserFindNameMatching.NameMatches(trimmed, name, matchFromStartOfName))
                    continue;
                list.Add(new BrowserFindMatch(file, name, BrowserFindMatchKind.File));
            }
        }

        list.Sort(static (a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        return list;
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
}
