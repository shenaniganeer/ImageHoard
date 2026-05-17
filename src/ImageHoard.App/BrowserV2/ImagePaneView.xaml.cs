using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageHoard.App;
using ImageHoard.Core.Browse;
using ImageHoard.Core.Browse2;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;

namespace ImageHoard.App.BrowserV2;

/// <summary>
/// Virtualized image list for the current browse folder, driven by <see cref="ImagePaneController"/>.
/// Uses <see cref="ScrollViewer"/> + <see cref="ItemsRepeater"/> and the same indexed pin + conditional Low restore as <see cref="FolderTreeView"/>.
/// </summary>
public sealed partial class ImagePaneView : UserControl
{
    /// <summary>Logical row height (must match item template <c>Height</c>).</summary>
    public const double ImageRowHeight = 36;

    private ImagePaneController? _controller;
    private int _rangeAnchorIndex = -1;
    private (int anchorIndex, double offsetInRowPx)? _pendingImageScrollRestore;

    public ImagePaneView()
    {
        InitializeComponent();
        ImageScrollViewer.SizeChanged += ImageScrollViewer_SizeChanged;
    }

    /// <summary>User right-clicked an image row after selection was applied; host shows the browser pane context menu.</summary>
    public event TypedEventHandler<ImagePaneView, BrowserPaneContextMenuRequestedEventArgs>? ContextMenuRequested;

    /// <summary>User completed an internal drag-move onto a folder target (tree row or current-folder drop zone).</summary>
    public event TypedEventHandler<ImagePaneView, BrowserPaneMoveDropRequestedEventArgs>? MoveDropRequested;

    /// <summary>Forwarded from the inner <see cref="ScrollViewer"/> (layout persistence).</summary>
    public event TypedEventHandler<ImagePaneView, ScrollViewerViewChangedEventArgs>? ScrollViewerViewChanged;

    public ImagePaneController? Controller
    {
        get => _controller;
        set
        {
            if (ReferenceEquals(_controller, value))
                return;
            DetachController();
            _controller = value;
            AttachController();
        }
    }

    /// <summary>Image list column chrome for size/date (Browse2).</summary>
    public void ApplyImageColumnVisibility(bool showSizeColumn, bool showDateColumn) =>
        _controller?.SetImageColumnVisibility(showSizeColumn, showDateColumn);

    /// <summary>
    /// Pins the row for <paramref name="fullPath"/> using the same <see cref="ListViewportScrollMath"/> contract as <see cref="FolderTreeView.ScrollFolderIntoView"/>.
    /// Low-priority restore runs only after an actual <see cref="ScrollViewer.ChangeView"/>.
    /// </summary>
    public void ScrollImagePathIntoView(
        string fullPath,
        bool centerInViewport = false,
        bool skipIfFullyVisible = false,
        int pinLeadingLineIndex = 0)
    {
        if (_controller is null)
            return;
        var ix = FindRowIndex(fullPath);
        if (ix < 0)
            return;
        ScrollImageIndexIntoView(ix, centerInViewport, skipIfFullyVisible, pinLeadingLineIndex);
    }

    private void AttachController()
    {
        if (_controller is null)
        {
            ImageRepeater.ItemsSource = null;
            _rangeAnchorIndex = -1;
            return;
        }

        ImageRepeater.ItemsSource = _controller.Items;
        _controller.ImagePaneItemsRebuiltKeepingSelection += Controller_ImagePaneItemsRebuiltKeepingSelection;
        _controller.SelectedImagePathsChanged += Controller_SelectedImagePathsChanged;
        SyncRepeaterSelectionFromController();
    }

    private void DetachController()
    {
        if (_controller is null)
            return;
        _controller.ImagePaneItemsRebuiltKeepingSelection -= Controller_ImagePaneItemsRebuiltKeepingSelection;
        _controller.SelectedImagePathsChanged -= Controller_SelectedImagePathsChanged;
        ImageRepeater.ItemsSource = null;
        _controller = null;
        _rangeAnchorIndex = -1;
        _pendingImageScrollRestore = null;
    }

    private void Controller_ImagePaneItemsRebuiltKeepingSelection(object? sender, EventArgs e) =>
        SyncRepeaterSelectionFromController();

    private void Controller_SelectedImagePathsChanged(object? sender, EventArgs e) =>
        SyncRepeaterSelectionFromController();

    private void SyncRepeaterSelectionFromController()
    {
        if (_controller is null)
            return;

        var primary = _controller.SelectedImagePath;
        _rangeAnchorIndex = string.IsNullOrEmpty(primary) ? -1 : FindRowIndex(primary);
        RefreshRealizedRowChrome();
        if (!string.IsNullOrEmpty(primary))
            ScrollImagePathIntoView(primary, centerInViewport: false, skipIfFullyVisible: true, pinLeadingLineIndex: 2);
    }

    private int FindRowIndex(string fullPath)
    {
        var n = FavoriteIndexRoots.NormalizeFavoritePath(fullPath);
        if (_controller is null)
            return -1;
        for (var i = 0; i < _controller.Items.Count; i++)
        {
            if (string.Equals(_controller.Items[i].FullPath, n, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private void ScrollImageIndexIntoView(int ix, bool centerInViewport, bool skipIfFullyVisible, int pinLeadingLineIndex)
    {
        _pendingImageScrollRestore = null;
        var rowH = ImageRowHeight;
        if (rowH <= 0 || _controller is null || ix < 0 || ix >= _controller.Items.Count)
            return;

        ImageScrollViewer.UpdateLayout();
        ImageRepeater.UpdateLayout();
        var v = ImageScrollViewer.VerticalOffset;
        var vh = ImageScrollViewer.ViewportHeight;
        var maxY = Math.Max(0, ImageScrollViewer.ScrollableHeight);
        var itemTop = ix * rowH;
        double pinY;
        if (centerInViewport && vh > rowH)
            pinY = (vh - rowH) / 2;
        else
            pinY = Math.Max(0, pinLeadingLineIndex) * rowH;

        var newY = ListViewportScrollMath.TryComputePinnedVerticalOffset(
            itemTop,
            rowH,
            v,
            vh,
            maxY,
            pinY,
            skipIfFullyVisible);
        if (newY is not { } y)
            return;

        ImageScrollViewer.ChangeView(null, y, null, disableAnimation: true);
        _pendingImageScrollRestore = (ix, y - itemTop);
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            RestorePendingImageScroll);
    }

    private void RestorePendingImageScroll()
    {
        if (_pendingImageScrollRestore is not { } pending)
            return;
        _pendingImageScrollRestore = null;
        var rowH = ImageRowHeight;
        if (rowH <= 0 || _controller is null)
            return;

        ImageScrollViewer.UpdateLayout();
        ImageRepeater.UpdateLayout();
        var y = pending.anchorIndex * rowH + pending.offsetInRowPx;
        var maxY = Math.Max(0, ImageScrollViewer.ScrollableHeight);
        y = FolderTreeViewportAnchorMath.ClampVerticalScrollTarget(y, maxY);
        ImageScrollViewer.ChangeView(null, y, null, disableAnimation: true);
    }

    private void ImageScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_controller is null || string.IsNullOrEmpty(_controller.SelectedImagePath))
            return;
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
            return;
        ScrollImagePathIntoView(_controller.SelectedImagePath, centerInViewport: false, skipIfFullyVisible: true, pinLeadingLineIndex: 2);
    }

    private void ImageScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        RefreshRealizedRowChrome();
        ScrollViewerViewChanged?.Invoke(this, e);
    }

    /// <summary>
    /// Plain click selects immediately (preview + controller) even when focus was in the folder tree.
    /// Ctrl/Shift follow the same multi-select rules as <see cref="FolderTreeView"/>.
    /// </summary>
    private void ImageRowGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_controller is null || sender is not FrameworkElement { DataContext: ImagePaneRow row } fe)
            return;

        if (e.GetCurrentPoint(fe).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
            return;

        var (ctrl, shift, _, _) = WinUiKeyboardInterop.GetModifierStates();
        if (!ctrl && !shift)
        {
            _controller.NotifySelectedFromView(row.FullPath);
            _rangeAnchorIndex = FindRowIndex(row.FullPath);
            Focus(FocusState.Pointer);
            return;
        }

        if (ctrl && !shift)
        {
            ToggleImagePathInSelection(row.FullPath);
            _rangeAnchorIndex = FindRowIndex(row.FullPath);
            Focus(FocusState.Pointer);
            return;
        }

        if (shift && !ctrl)
        {
            var endIx = FindRowIndex(row.FullPath);
            if (_rangeAnchorIndex < 0 || endIx < 0)
            {
                _controller.NotifySelectedFromView(row.FullPath);
                _rangeAnchorIndex = endIx;
            }
            else
            {
                var paths = BuildPathsInIndexRange(_rangeAnchorIndex, endIx);
                _controller.NotifySelectionFromView(paths, row.FullPath);
            }

            Focus(FocusState.Pointer);
            return;
        }

        ToggleImagePathInSelection(row.FullPath);
        _rangeAnchorIndex = FindRowIndex(row.FullPath);
        Focus(FocusState.Pointer);
    }

    private void ToggleImagePathInSelection(string fullPath)
    {
        if (_controller is null)
            return;

        var n = FavoriteIndexRoots.NormalizeFavoritePath(fullPath);
        var set = new HashSet<string>(_controller.GetSelectedImagePathsSnapshot(), StringComparer.OrdinalIgnoreCase);
        var wasIn = set.Contains(n);
        if (wasIn)
            set.Remove(n);
        else
            set.Add(n);

        if (set.Count == 0)
        {
            _controller.NotifySelectedFromView(null);
            return;
        }

        var ordered = new List<string>();
        foreach (var item in _controller.Items)
        {
            var p = FavoriteIndexRoots.NormalizeFavoritePath(item.FullPath);
            if (set.Contains(p))
                ordered.Add(p);
        }

        if (ordered.Count == 0)
        {
            _controller.NotifySelectedFromView(null);
            return;
        }

        var primary = wasIn ? ordered[^1] : n;
        _controller.NotifySelectionFromView(ordered, primary);
    }

    private List<string> BuildPathsInIndexRange(int anchorIx, int endIx)
    {
        var list = new List<string>();
        if (_controller is null)
            return list;
        var a = Math.Min(anchorIx, endIx);
        var b = Math.Max(anchorIx, endIx);
        for (var i = a; i <= b && i < _controller.Items.Count; i++)
            list.Add(FavoriteIndexRoots.NormalizeFavoritePath(_controller.Items[i].FullPath));

        return list;
    }

    private void ImageRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not FrameworkElement fe)
            return;
        var ix = args.Index;
        if (_controller is null || ix < 0 || ix >= _controller.Items.Count)
            return;
        var row = _controller.Items[ix];
        fe.DataContext = row;
        AutomationProperties.SetName(fe, row.DisplayName + ", image file");
        ApplySelectionChrome(fe, row.FullPath);
    }

    private void ImageRepeater_ElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is FrameworkElement fe)
            fe.DataContext = null;
    }

    private void ApplySelectionChrome(FrameworkElement rowRoot, string path)
    {
        if (_controller is null || rowRoot is not Panel panel)
            return;

        var n = FavoriteIndexRoots.NormalizeFavoritePath(path);
        var primary = !string.IsNullOrEmpty(_controller.SelectedImagePath)
                      && string.Equals(n, _controller.SelectedImagePath, StringComparison.OrdinalIgnoreCase);
        var multi = _controller.IsImagePathSelected(path);
        if (rowRoot.FindName("SelectionIndicator") is UIElement indicator)
            indicator.Visibility = primary || multi ? Visibility.Visible : Visibility.Collapsed;

        if (primary)
            panel.Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        else if (multi)
            panel.Background = (Brush)Application.Current.Resources["SubtleFillColorTertiaryBrush"];
        else
            panel.ClearValue(Panel.BackgroundProperty);
    }

    private void RefreshRealizedRowChrome()
    {
        if (_controller is null)
            return;

        var rowH = ImageRowHeight;
        if (rowH <= 0 || _controller.Items.Count == 0)
            return;

        var first = (int)Math.Floor(ImageScrollViewer.VerticalOffset / rowH);
        var last = (int)Math.Ceiling((ImageScrollViewer.VerticalOffset + ImageScrollViewer.ViewportHeight) / rowH);
        first = Math.Clamp(first, 0, _controller.Items.Count - 1);
        last = Math.Clamp(last, 0, _controller.Items.Count - 1);
        for (var i = first; i <= last; i++)
        {
            var el = ImageRepeater.TryGetElement(i);
            if (el is not FrameworkElement fe)
                continue;
            var row = fe.DataContext as ImagePaneRow;
            if (row is null && i >= 0 && i < _controller.Items.Count)
            {
                row = _controller.Items[i];
                fe.DataContext = row;
            }

            if (row is not null)
                ApplySelectionChrome(fe, row.FullPath);
        }
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_controller is null || _controller.Items.Count == 0)
            return;

        var primary = _controller.SelectedImagePath;
        var ix = string.IsNullOrEmpty(primary) ? -1 : FindRowIndex(primary);
        if (ix < 0)
            ix = 0;

        switch (e.Key)
        {
            case VirtualKey.Down:
                if (ix + 1 < _controller.Items.Count)
                {
                    var p = _controller.Items[ix + 1].FullPath;
                    _controller.NotifySelectedFromView(p);
                    _rangeAnchorIndex = ix + 1;
                }

                e.Handled = true;
                break;
            case VirtualKey.Up:
                if (ix > 0)
                {
                    var p = _controller.Items[ix - 1].FullPath;
                    _controller.NotifySelectedFromView(p);
                    _rangeAnchorIndex = ix - 1;
                }

                e.Handled = true;
                break;
            case VirtualKey.Home:
            {
                var p = _controller.Items[0].FullPath;
                _controller.NotifySelectedFromView(p);
                _rangeAnchorIndex = 0;
                e.Handled = true;
                break;
            }
            case VirtualKey.End:
            {
                var last = _controller.Items.Count - 1;
                var p = _controller.Items[last].FullPath;
                _controller.NotifySelectedFromView(p);
                _rangeAnchorIndex = last;
                e.Handled = true;
                break;
            }
        }
    }

    private void ImageRowGrid_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ImagePaneRow row } anchor || _controller is null)
            return;

        _controller.NotifySelectedFromView(row.FullPath);
        _rangeAnchorIndex = FindRowIndex(row.FullPath);
        ContextMenuRequested?.Invoke(this, new BrowserPaneContextMenuRequestedEventArgs(anchor, e));
    }

    private void ImageRowGrid_DragStarting(UIElement sender, DragStartingEventArgs e)
    {
        if (_controller is null || sender is not FrameworkElement { DataContext: ImagePaneRow row })
            return;

        var dragSet = _controller.IsImagePathSelected(row.FullPath)
            ? _controller.GetSelectedImagePathsSnapshot()
            : new[] { row.FullPath };

        if (dragSet.Count == 0)
        {
            e.Cancel = true;
            return;
        }

        var payload = string.Join('\n', dragSet.Select(p => FavoriteIndexRoots.NormalizeFavoritePath(p)));
        e.Data.SetData(BrowserPaneDragDropFormats.PathListV1, payload);
        e.Data.Properties.Title = dragSet.Count == 1 ? Path.GetFileName(dragSet[0]) : $"{dragSet.Count} items";
        e.AllowedOperations = DataPackageOperation.Move;
    }

    private void ImagePaneRoot_DragOver(object sender, DragEventArgs e)
    {
        if (_controller is null || string.IsNullOrEmpty(_controller.CurrentFolderPath))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        if (!e.DataView.Contains(BrowserPaneDragDropFormats.PathListV1))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        e.Handled = true;
    }

    private async void ImagePaneRoot_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_controller is null || string.IsNullOrEmpty(_controller.CurrentFolderPath))
            return;

        if (!e.DataView.Contains(BrowserPaneDragDropFormats.PathListV1))
            return;

        var raw = await e.DataView.GetTextAsync(BrowserPaneDragDropFormats.PathListV1);
        var sources = ParsePathLines(raw);
        if (sources.Count == 0)
            return;

        MoveDropRequested?.Invoke(
            this,
            new BrowserPaneMoveDropRequestedEventArgs
            {
                SourcePaths = sources,
                DestinationDirectory = FavoriteIndexRoots.NormalizeFavoritePath(_controller.CurrentFolderPath),
            });
    }

    public static IReadOnlyList<string> ParsePathLines(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        var lines = raw.Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            var n = FavoriteIndexRoots.NormalizeFavoritePath(line);
            if (!string.IsNullOrEmpty(n))
                list.Add(n);
        }

        return list;
    }
}
