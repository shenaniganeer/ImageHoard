using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageHoard.Core.Browse;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
