using System.Collections.ObjectModel;
using ImageHoard.App;
using ImageHoard.Core.Browse;
using ImageHoard.Core.Browse2;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace ImageHoard.App.BrowserV2;

/// <summary>
/// Virtualized folders-only tree: <see cref="ItemsRepeater"/> inside a <see cref="ScrollViewer"/>.
/// Applies scroll anchoring (capture top visible row + intra-row offset, apply delta, restore) on each model delta.
/// </summary>
public sealed partial class FolderTreeView : UserControl
{
    public const double DefaultRowHeight = 32;

    private readonly ObservableCollection<FolderRow> _rows = new();

    private string? _contextMenuPath;
    private (int anchorIndex, double offsetInRowPx)? _pendingScrollRestore;
    private bool _suspendSelectedPathCallback;
    private Visibility _folderSizeColumnVisibility = Visibility.Visible;
    private Visibility _folderImageCountColumnVisibility = Visibility.Visible;
    private Visibility _folderDateColumnVisibility = Visibility.Visible;

    public FolderTreeView()
    {
        InitializeComponent();
        TreeRepeater.ItemsSource = _rows;
        TreeScrollViewer.ViewChanged += OnTreeScrollViewerViewChanged;
    }

    /// <summary>Index root for parent-walk when an anchor path no longer exists.</summary>
    public string? IndexRoot
    {
        get => (string?)GetValue(IndexRootProperty);
        set => SetValue(IndexRootProperty, value);
    }

    public static readonly DependencyProperty IndexRootProperty =
        DependencyProperty.Register(nameof(IndexRoot), typeof(string), typeof(FolderTreeView), new PropertyMetadata(null));

    public string? SelectedFolderPath
    {
        get => (string?)GetValue(SelectedFolderPathProperty);
        set => SetValue(SelectedFolderPathProperty, value);
    }

    public static readonly DependencyProperty SelectedFolderPathProperty =
        DependencyProperty.Register(
            nameof(SelectedFolderPath),
            typeof(string),
            typeof(FolderTreeView),
            new PropertyMetadata(null, OnSelectedFolderPathPropertyChangedStatic));

    private static void OnSelectedFolderPathPropertyChangedStatic(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((FolderTreeView)d).OnSelectedFolderPathPropertyChanged((string?)e.NewValue);

    public event TypedEventHandler<FolderTreeView, string>? ToggleExpandRequested;

    public event TypedEventHandler<FolderTreeView, string>? OpenInExplorerRequested;

    public event TypedEventHandler<FolderTreeView, string>? SelectedFolderPathChanged;

    /// <summary>Forwarded from the inner <see cref="ScrollViewer"/> for layout persistence.</summary>
    public event TypedEventHandler<FolderTreeView, ScrollViewerViewChangedEventArgs>? ScrollViewerViewChanged;

    /// <summary>Logical row height used for scroll math (must match item template height).</summary>
    public double RowHeight { get; set; } = DefaultRowHeight;

    /// <summary>Pixels of indent per tree depth level.</summary>
    public double IndentPerDepthPx { get; set; } = 16;

    /// <summary>Metrics column visibility (follows View → browser folder column toggles).</summary>
    public void SetMetricsColumnsVisibility(bool showSize, bool showImageCount, bool showDate)
    {
        _folderSizeColumnVisibility = showSize ? Visibility.Visible : Visibility.Collapsed;
        _folderImageCountColumnVisibility = showImageCount ? Visibility.Visible : Visibility.Collapsed;
        _folderDateColumnVisibility = showDate ? Visibility.Visible : Visibility.Collapsed;
        RefreshRealizedRowChrome();
    }

    /// <summary>
    /// Applies a flat-model delta to the repeater. When <paramref name="preserveViewport"/> is true,
    /// captures the top visible row and vertical offset within that row, then restores after layout.
    /// </summary>
    public void ApplyModelDelta(FlatModelDelta delta, bool preserveViewport = true)
    {
        if (delta.IsEmpty)
            return;

        double vOffset = TreeScrollViewer.VerticalOffset;
        double rowH = RowHeight;
        string? anchorPath = null;
        double offsetInRow = 0;

        if (preserveViewport && _rows.Count > 0 && rowH > 0)
        {
            if (FolderTreeViewportAnchorMath.TryCaptureTopVisibleRow(vOffset, rowH, _rows.Count, i => _rows[i].Path) is { } cap)
            {
                anchorPath = cap.AnchorPath;
                offsetInRow = cap.OffsetInRowPx;
            }
        }

        ApplyDeltaToRows(delta);

        if (!preserveViewport || anchorPath is null)
        {
            RefreshRealizedRowChrome();
            return;
        }

        var resolved = FolderTreeViewportAnchorMath.ResolveRowIndexForAnchor(
            anchorPath,
            IndexRoot,
            _rows.Count,
            i => _rows[i].Path);
        _pendingScrollRestore = (resolved, offsetInRow);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, RestorePendingScroll);
        RefreshRealizedRowChrome();
    }

    /// <summary>Replaces all rows (full reset) with optional viewport preservation.</summary>
    public void ResetRows(IReadOnlyList<FolderRow> rows, bool preserveViewport = false) =>
        ApplyModelDelta(new FlatModelDelta(new[] { new FlatModelReset(rows) }), preserveViewport);

    public void ScrollFolderIntoView(string folderPath, bool centerInViewport = false)
    {
        // Drop deferred preserveViewport restore so a queued Low-priority RestorePendingScroll cannot
        // overwrite this explicit scroll (e.g. RevealAndSelect then ScrollFolderIntoView on cold boot / Find).
        _pendingScrollRestore = null;
        var ix = FindRowIndex(folderPath);
        if (ix < 0)
            return;
        var rowH = RowHeight;
        if (rowH <= 0)
            return;
        // Force layout so the inner ItemsRepeater has measured its extent before ChangeView; otherwise
        // on cold boot ScrollableHeight is still 0 and ChangeView silently clamps the target to the top.
        TreeScrollViewer.UpdateLayout();
        TreeRepeater.UpdateLayout();
        // Negative intra-row offset is fine: RestorePendingScroll re-evaluates the y as
        // (ix * rowH + offset) and re-clamps against the current ScrollableHeight.
        var offset = 0.0;
        if (centerInViewport && TreeScrollViewer.ViewportHeight > rowH)
            offset = -(TreeScrollViewer.ViewportHeight - rowH) / 2;
        var y = ix * rowH + offset;
        var maxY = Math.Max(0, TreeScrollViewer.ScrollableHeight);
        y = FolderTreeViewportAnchorMath.ClampVerticalScrollTarget(y, maxY);
        TreeScrollViewer.ChangeView(null, y, null, disableAnimation: true);
        // Belt-and-braces: re-apply at Low priority after layout settles. If layout was still in
        // progress and ScrollableHeight clamped y down, the deferred pass re-evaluates against the
        // final extent once rows are realized.
        _pendingScrollRestore = (ix, offset);
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, RestorePendingScroll);
    }

    /// <summary>Restores vertical scroll from persisted <c>paths.browserTree.viewportAnchor</c> after cold-boot row reset.</summary>
    public void RestorePersistedViewportAnchor(string? anchorFolderPath, double offsetWithinRowPx)
    {
        if (_rows.Count == 0 || RowHeight <= 0)
            return;

        if (string.IsNullOrWhiteSpace(anchorFolderPath))
        {
            TreeScrollViewer.ChangeView(null, 0, null, disableAnimation: true);
            return;
        }

        var rowH = RowHeight;
        var ix = FolderTreeViewportAnchorMath.ResolveRowIndexForAnchor(
            FavoriteIndexRoots.NormalizeFavoritePath(anchorFolderPath),
            IndexRoot,
            _rows.Count,
            i => _rows[i].Path);
        var off = offsetWithinRowPx;
        if (double.IsNaN(off) || double.IsInfinity(off) || off < 0)
            off = 0;
        if (off > rowH)
            off = rowH;
        _pendingScrollRestore = (ix, off);
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, RestorePendingScroll);
    }

    /// <summary>Top visible folder row + intra-row offset for persistence (matches scroll-anchor capture in <see cref="ApplyModelDelta"/>).</summary>
    internal ViewportAnchorDto? GetPersistedViewportAnchor()
    {
        if (_rows.Count == 0 || RowHeight <= 0)
            return null;
        var rowH = RowHeight;
        var vOffset = TreeScrollViewer.VerticalOffset;
        if (FolderTreeViewportAnchorMath.TryCaptureTopVisibleRow(vOffset, rowH, _rows.Count, i => _rows[i].Path) is not { } cap)
            return null;
        return new ViewportAnchorDto
        {
            AnchorFolderPath = cap.AnchorPath,
            OffsetWithinRowPx = cap.OffsetInRowPx,
        };
    }

    private void OnTreeScrollViewerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        RefreshRealizedRowChrome();
        ScrollViewerViewChanged?.Invoke(this, e);
    }

    private void OnSelectedFolderPathPropertyChanged(string? newPath)
    {
        if (_suspendSelectedPathCallback)
            return;
        RefreshRealizedRowChrome();
        SelectedFolderPathChanged?.Invoke(this, newPath ?? "");
    }

    private void RestorePendingScroll()
    {
        if (_pendingScrollRestore is not { } pending)
            return;
        _pendingScrollRestore = null;
        var rowH = RowHeight;
        if (rowH <= 0)
            return;
        TreeScrollViewer.UpdateLayout();
        TreeRepeater.UpdateLayout();
        var y = pending.anchorIndex * rowH + pending.offsetInRowPx;
        var maxY = Math.Max(0, TreeScrollViewer.ScrollableHeight);
        y = FolderTreeViewportAnchorMath.ClampVerticalScrollTarget(y, maxY);
        TreeScrollViewer.ChangeView(null, y, null, disableAnimation: true);
    }

    private static string ParentPathOrEmpty(string fullPath, string? indexRoot)
    {
        var n = FavoriteIndexRoots.NormalizeFavoritePath(fullPath);
        var r = string.IsNullOrEmpty(indexRoot) ? null : FavoriteIndexRoots.NormalizeFavoritePath(indexRoot);
        if (r is not null && string.Equals(n, r, StringComparison.OrdinalIgnoreCase))
            return "";
        var p = Path.GetDirectoryName(n);
        return string.IsNullOrEmpty(p) ? "" : FavoriteIndexRoots.NormalizeFavoritePath(p);
    }

    private int FindRowIndex(string path)
    {
        var n = FavoriteIndexRoots.NormalizeFavoritePath(path);
        for (var i = 0; i < _rows.Count; i++)
        {
            if (string.Equals(_rows[i].Path, n, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private void ApplyDeltaToRows(FlatModelDelta delta)
    {
        foreach (var ch in delta.Changes)
        {
            switch (ch)
            {
                case FlatModelReset reset:
                    _rows.Clear();
                    foreach (var row in reset.Rows)
                        _rows.Add(row);
                    return;
                case FlatModelRemoveRange rr:
                    for (var i = 0; i < rr.Count; i++)
                        _rows.RemoveAt(rr.StartIndex);
                    break;
                case FlatModelInsertRange ir:
                    for (var i = 0; i < ir.Rows.Count; i++)
                        _rows.Insert(ir.StartIndex + i, ir.Rows[i]);
                    break;
                case FlatModelReplaceRow rep:
                    _rows[rep.Index] = rep.Row;
                    break;
            }
        }
    }

    private void Root_GotFocus(object sender, RoutedEventArgs e) =>
        OuterBorder.BorderBrush = Application.Current.Resources["AccentFillColorDefaultBrush"] as Brush
                                  ?? OuterBorder.BorderBrush;

    private void Root_LosingFocus(UIElement sender, LosingFocusEventArgs args)
    {
        if (args.NewFocusedElement is DependencyObject o && IsDescendantOf(o, this))
            return;
        OuterBorder.BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
    }

    private static bool IsDescendantOf(DependencyObject? node, DependencyObject ancestor)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, ancestor))
                return true;
            node = VisualTreeHelper.GetParent(node);
        }

        return false;
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_rows.Count == 0)
            return;

        var current = SelectedFolderPath;
        var ix = string.IsNullOrEmpty(current) ? -1 : FindRowIndex(current);
        if (ix < 0)
            ix = Math.Min(0, _rows.Count - 1);

        switch (e.Key)
        {
            case Windows.System.VirtualKey.Down:
                if (ix + 1 < _rows.Count)
                    SetSelectedPathFromUi(_rows[ix + 1].Path, scrollIntoView: true);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Up:
                if (ix > 0)
                    SetSelectedPathFromUi(_rows[ix - 1].Path, scrollIntoView: true);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Home:
                SetSelectedPathFromUi(_rows[0].Path, scrollIntoView: true);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.End:
                SetSelectedPathFromUi(_rows[^1].Path, scrollIntoView: true);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Right:
            {
                var row = _rows[Math.Clamp(ix, 0, _rows.Count - 1)];
                if (row.HasChildren && !row.IsExpanded)
                    ToggleExpandRequested?.Invoke(this, row.Path);
                e.Handled = true;
                break;
            }
            case Windows.System.VirtualKey.Left:
            {
                var row = _rows[Math.Clamp(ix, 0, _rows.Count - 1)];
                if (row.HasChildren && row.IsExpanded)
                    ToggleExpandRequested?.Invoke(this, row.Path);
                else
                {
                    var parent = ParentPathOrEmpty(row.Path, IndexRoot);
                    if (!string.IsNullOrEmpty(parent) && FindRowIndex(parent) >= 0)
                        SetSelectedPathFromUi(parent, scrollIntoView: true);
                }

                e.Handled = true;
                break;
            }
            case Windows.System.VirtualKey.Enter:
            case Windows.System.VirtualKey.Space:
            {
                var row = _rows[Math.Clamp(ix, 0, _rows.Count - 1)];
                if (row.HasChildren)
                    ToggleExpandRequested?.Invoke(this, row.Path);
                e.Handled = true;
                break;
            }
        }
    }

    private void SetSelectedPathFromUi(string path, bool scrollIntoView)
    {
        _suspendSelectedPathCallback = true;
        SelectedFolderPath = path;
        _suspendSelectedPathCallback = false;
        SelectedFolderPathChanged?.Invoke(this, path);
        RefreshRealizedRowChrome();
        if (scrollIntoView)
            ScrollFolderIntoView(path, centerInViewport: false);
    }

    private void FolderRow_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement root)
            return;
        if (IsExpandToggleHostFromEventSource(e.OriginalSource as DependencyObject))
            return;
        if (root.DataContext is not FolderRow row)
            return;
        SetSelectedPathFromUi(row.Path, scrollIntoView: false);
        Focus(FocusState.Pointer);
    }

    private void FolderRow_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FolderRow row })
            return;
        if (IsExpandToggleHostFromEventSource(e.OriginalSource as DependencyObject))
            return;
        OpenInExplorerRequested?.Invoke(this, row.Path);
        _ = global::Windows.System.Launcher.LaunchFolderPathAsync(row.Path);
    }

    private void FolderRow_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FolderRow row })
            return;
        SetSelectedPathFromUi(row.Path, scrollIntoView: false);
    }

    private void ExpandToggleHost_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FolderRow row })
            return;
        if (!row.HasChildren)
            return;
        e.Handled = true;
        ToggleExpandRequested?.Invoke(this, row.Path);
        Focus(FocusState.Pointer);
    }

    private void FolderRowContextFlyout_Opening(object? sender, object e)
    {
        if (sender is not MenuFlyout mf || mf.Target is not FrameworkElement t || t.DataContext is not FolderRow row)
            return;
        _contextMenuPath = row.Path;
        foreach (var item in mf.Items)
        {
            if (item is not MenuFlyoutItem mi)
                continue;
            var tag = mi.Tag as string;
            if (tag == "expand")
                mi.Visibility = row is { HasChildren: true, IsExpanded: false } ? Visibility.Visible : Visibility.Collapsed;
            else if (tag == "collapse")
                mi.Visibility = row is { HasChildren: true, IsExpanded: true } ? Visibility.Visible : Visibility.Collapsed;
            else if (tag == "explorer")
                mi.Visibility = Visibility.Visible;
        }
    }

    private void CtxMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string tag } || _contextMenuPath is not { } p)
            return;
        switch (tag)
        {
            case "expand":
            case "collapse":
                ToggleExpandRequested?.Invoke(this, p);
                break;
            case "explorer":
                OpenInExplorerRequested?.Invoke(this, p);
                _ = global::Windows.System.Launcher.LaunchFolderPathAsync(p);
                break;
        }
    }

    private void TreeRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not FrameworkElement fe)
            return;
        var ix = args.Index;
        if (ix < 0 || ix >= _rows.Count)
            return;
        var row = _rows[ix];
        fe.DataContext = row;
        ApplyRowLayoutAndAutomation(fe, row);
        ApplySelectionChrome(fe, row.Path);
    }

    private void TreeRepeater_ElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is FrameworkElement fe)
            fe.DataContext = null;
    }

    private void ApplyRowLayoutAndAutomation(FrameworkElement rowRoot, FolderRow row)
    {
        if (rowRoot.FindName("IndentHost") is FrameworkElement indent)
            indent.Width = Math.Max(0, row.Depth) * IndentPerDepthPx;

        // Keep expand column width for all rows so folder names align; hide only the glyph for leaves.
        if (rowRoot.FindName("ExpandToggleHost") is UIElement expandHost)
        {
            expandHost.Visibility = Visibility.Visible;
            expandHost.IsHitTestVisible = row.HasChildren;
            AutomationProperties.SetAccessibilityView(
                expandHost,
                row.HasChildren ? AccessibilityView.Control : AccessibilityView.Raw);
        }

        if (rowRoot.FindName("ChevronIcon") is FontIcon chevron)
        {
            chevron.Visibility = row.HasChildren ? Visibility.Visible : Visibility.Collapsed;
            chevron.Glyph = row.IsExpanded ? "\uE70D" : "\uE76C";
        }

        if (rowRoot.FindName("SizeText") is UIElement sizeEl)
            sizeEl.Visibility = _folderSizeColumnVisibility;
        if (rowRoot.FindName("ImageCountText") is UIElement imgEl)
            imgEl.Visibility = _folderImageCountColumnVisibility;
        if (rowRoot.FindName("DateText") is UIElement dateEl)
            dateEl.Visibility = _folderDateColumnVisibility;

        var state = row.IsExpanded ? "expanded" : "collapsed";
        var name = $"{row.Name}, folder, {state}, depth {row.Depth}";
        AutomationProperties.SetName(rowRoot, name);
    }

    private void ApplySelectionChrome(FrameworkElement rowRoot, string path)
    {
        if (rowRoot is not Panel panel)
            return;
        var sel = SelectedFolderPath;
        var selected = !string.IsNullOrEmpty(sel) &&
                       string.Equals(path, sel, StringComparison.OrdinalIgnoreCase);
        if (rowRoot.FindName("SelectionIndicator") is UIElement indicator)
            indicator.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;

        if (selected)
            panel.Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        else
            panel.ClearValue(Panel.BackgroundProperty);
    }

    private void RefreshRealizedRowChrome()
    {
        var rowH = RowHeight;
        if (rowH <= 0 || _rows.Count == 0)
            return;
        var first = (int)Math.Floor(TreeScrollViewer.VerticalOffset / rowH);
        var last = (int)Math.Ceiling((TreeScrollViewer.VerticalOffset + TreeScrollViewer.ViewportHeight) / rowH);
        first = Math.Clamp(first, 0, _rows.Count - 1);
        last = Math.Clamp(last, 0, _rows.Count - 1);
        for (var i = first; i <= last; i++)
        {
            var el = TreeRepeater.TryGetElement(i);
            if (el is not FrameworkElement fe)
                continue;
            var row = fe.DataContext as FolderRow;
            if (row is null && i >= 0 && i < _rows.Count)
            {
                row = _rows[i];
                fe.DataContext = row;
            }

            if (row is not null)
            {
                ApplyRowLayoutAndAutomation(fe, row);
                ApplySelectionChrome(fe, row.Path);
            }
        }
    }

    private static bool IsExpandToggleHostFromEventSource(DependencyObject? src)
    {
        while (src is not null)
        {
            if (src is FrameworkElement fe && fe.Name == "ExpandToggleHost")
                return true;
            src = VisualTreeHelper.GetParent(src);
        }

        return false;
    }
}
