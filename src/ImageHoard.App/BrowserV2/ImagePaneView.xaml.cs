using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageHoard.Core.Browse;
using ImageHoard.Core.Browse2;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace ImageHoard.App.BrowserV2;

/// <summary>
/// Virtualized image list for the current browse folder, driven by <see cref="ImagePaneController"/>.
/// </summary>
public sealed partial class ImagePaneView : UserControl
{
    private ImagePaneController? _controller;
    private bool _suppressSelectionSync;

    public ImagePaneView()
    {
        InitializeComponent();
    }

    /// <summary>User right-clicked an image row after selection was applied; host shows the browser pane context menu.</summary>
    public event TypedEventHandler<ImagePaneView, BrowserPaneContextMenuRequestedEventArgs>? ContextMenuRequested;

    /// <summary>User completed an internal drag-move onto a folder target (tree row or current-folder drop zone).</summary>
    public event TypedEventHandler<ImagePaneView, BrowserPaneMoveDropRequestedEventArgs>? MoveDropRequested;

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

    private void AttachController()
    {
        if (_controller is null)
        {
            ImageList.ItemsSource = null;
            return;
        }

        ImageList.ItemsSource = _controller.Items;
        _controller.SelectedImagePathChanged += Controller_SelectedImagePathChanged;
        _controller.ImagePaneItemsRebuiltKeepingSelection += Controller_ImagePaneItemsRebuiltKeepingSelection;
        _controller.SelectedImagePathsChanged += Controller_SelectedImagePathsChanged;
        SyncListSelectionFromController();
    }

    private void DetachController()
    {
        if (_controller is null)
            return;
        _controller.SelectedImagePathChanged -= Controller_SelectedImagePathChanged;
        _controller.ImagePaneItemsRebuiltKeepingSelection -= Controller_ImagePaneItemsRebuiltKeepingSelection;
        _controller.SelectedImagePathsChanged -= Controller_SelectedImagePathsChanged;
        ImageList.ItemsSource = null;
        _controller = null;
    }

    private void Controller_SelectedImagePathChanged(object? sender, string? e) =>
        SyncListSelectionFromController();

    private void Controller_ImagePaneItemsRebuiltKeepingSelection(object? sender, EventArgs e) =>
        SyncListSelectionFromController();

    private void Controller_SelectedImagePathsChanged(object? sender, EventArgs e) =>
        SyncListSelectionFromController();

    private void SyncListSelectionFromController()
    {
        if (_controller is null)
            return;

        _suppressSelectionSync = true;
        try
        {
            ImageList.SelectedItems.Clear();
            string? primary = _controller.SelectedImagePath;
            ImagePaneRow? primaryRow = null;
            foreach (var row in _controller.Items)
            {
                if (_controller.IsImagePathSelected(row.FullPath))
                    ImageList.SelectedItems.Add(row);
                if (!string.IsNullOrEmpty(primary)
                    && string.Equals(row.FullPath, primary, StringComparison.OrdinalIgnoreCase))
                    primaryRow = row;
            }

            if (primaryRow is not null)
                ImageList.SelectedItem = primaryRow;
            else if (ImageList.SelectedItems.Count == 0)
                ImageList.SelectedItem = null;
        }
        finally
        {
            _suppressSelectionSync = false;
        }

        SchedulePrimaryRowViewportScroll();
    }

private void SchedulePrimaryRowViewportScroll()
    {
        _ = DispatcherQueue.GetForCurrentThread()?.TryEnqueue(DispatcherQueuePriority.Normal, () => ApplyPrimaryRowViewportScroll(0));
    }

    /// <param name="retryDepth">0 = first pass; 1 = one deferred retry when virtualization has not yet produced a container.</param>
    private void ApplyPrimaryRowViewportScroll(int retryDepth)
    {
        const int maxRetryDepth = 1;
        if (_controller is null || string.IsNullOrEmpty(_controller.SelectedImagePath))
            return;

        ImagePaneRow? primaryRow = null;
        foreach (var row in _controller.Items)
        {
            if (string.Equals(row.FullPath, _controller.SelectedImagePath, StringComparison.OrdinalIgnoreCase))
            {
                primaryRow = row;
                break;
            }
        }

        if (primaryRow is null)
            return;

        // Realize off-screen virtualized rows before ContainerFromItem / TransformToVisual.
        ImageList.ScrollIntoView(primaryRow);
        ImageList.UpdateLayout();

        if (ImageList.ContainerFromItem(primaryRow) is not ListViewItem lvi)
        {
            if (retryDepth < maxRetryDepth)
                DispatcherQueue.GetForCurrentThread()?.TryEnqueue(DispatcherQueuePriority.Normal, () => ApplyPrimaryRowViewportScroll(retryDepth + 1));
            return;
        }

        var sv = FindDescendantScrollViewer(ImageList);
        if (sv is null)
            return;

        var rowRoot = (lvi.ContentTemplateRoot as FrameworkElement) ?? lvi;
        if (rowRoot.ActualHeight <= 0)
            rowRoot.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var height = rowRoot.ActualHeight > 0 ? rowRoot.ActualHeight : 32;
        var pt = rowRoot.TransformToVisual(sv).TransformPoint(new Point(0, 0));
        var itemContentTop = sv.VerticalOffset + pt.Y;
        var pinY = 2 * height;
        var newY = ListViewportScrollMath.TryComputePinnedVerticalOffset(
            itemContentTop,
            height,
            sv.VerticalOffset,
            sv.ViewportHeight,
            Math.Max(0, sv.ScrollableHeight),
            pinY,
            skipIfFullyVisible: true);
        if (newY is { } y)
            sv.ChangeView(null, y, null, disableAnimation: true);
    }

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv)
            return sv;
        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            var found = FindDescendantScrollViewer(c);
            if (found is not null)
                return found;
        }

        return null;
    }

    private void ImageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionSync || _controller is null)
            return;

        var paths = new List<string>(ImageList.SelectedItems.Count);
        foreach (var o in ImageList.SelectedItems)
        {
            if (o is ImagePaneRow r)
                paths.Add(r.FullPath);
        }

        if (paths.Count == 0)
        {
            _controller.NotifySelectedFromView(null);
            return;
        }

        string? primary = null;
        if (ImageList.SelectedItem is ImagePaneRow focus)
            primary = focus.FullPath;
        else if (e.AddedItems.Count > 0 && e.AddedItems[^1] is ImagePaneRow added)
            primary = added.FullPath;
        else
            primary = paths[^1];

        _controller.NotifySelectionFromView(paths, primary);
        SchedulePrimaryRowViewportScroll();
    }

    private void ImageRowGrid_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ImagePaneRow row } anchor || _controller is null)
            return;

        _suppressSelectionSync = true;
        try
        {
            ImageList.SelectedItems.Clear();
            ImageList.SelectedItem = row;
        }
        finally
        {
            _suppressSelectionSync = false;
        }

        _controller.NotifySelectedFromView(row.FullPath);
        SchedulePrimaryRowViewportScroll();
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
