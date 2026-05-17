using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
        SyncListSelectionFromController();
    }

    private void DetachController()
    {
        if (_controller is null)
            return;
        _controller.SelectedImagePathChanged -= Controller_SelectedImagePathChanged;
        _controller.ImagePaneItemsRebuiltKeepingSelection -= Controller_ImagePaneItemsRebuiltKeepingSelection;
        ImageList.ItemsSource = null;
        _controller = null;
    }

    private void Controller_SelectedImagePathChanged(object? sender, string? e) =>
        SyncListSelectionFromController();

    private void Controller_ImagePaneItemsRebuiltKeepingSelection(object? sender, EventArgs e) =>
        SyncListSelectionFromController();

    private void SyncListSelectionFromController()
    {
        if (_controller is null)
            return;

        _suppressSelectionSync = true;
        try
        {
            var target = _controller.SelectedImagePath;
            if (string.IsNullOrEmpty(target))
            {
                ImageList.SelectedItem = null;
                return;
            }

            ImagePaneRow? found = null;
            foreach (var row in _controller.Items)
            {
                if (string.Equals(row.FullPath, target, StringComparison.OrdinalIgnoreCase))
                {
                    found = row;
                    break;
                }
            }

            ImageList.SelectedItem = found;
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
        var path = (ImageList.SelectedItem as ImagePaneRow)?.FullPath;
        _controller.NotifySelectedFromView(path);
    }
}
