using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace ImageHoard.App.BrowserV2;

/// <summary>Swappable browse chrome: virtualized folder tree + image list (Browse2).</summary>
public sealed partial class BrowserV2Host : UserControl
{
    private bool _folderImageSplitDrag;
    private double _folderImageSplitPressX;
    private double _folderImageInitFolderW;
    private double _folderImageInitImageW;

    public BrowserV2Host()
    {
        InitializeComponent();
        BrowseFolderTree.ContextMenuRequested += OnFolderTreeContextMenuRequested;
        BrowseImagePane.ContextMenuRequested += OnImagePaneContextMenuRequested;
    }

    /// <summary>Right-click on folder tree or image list after row selection; app shows the shared browser context menu.</summary>
    public event TypedEventHandler<BrowserV2Host, BrowserPaneContextMenuRequestedEventArgs>? BrowserPaneContextMenuRequested;

    private void OnFolderTreeContextMenuRequested(FolderTreeView sender, BrowserPaneContextMenuRequestedEventArgs e) =>
        BrowserPaneContextMenuRequested?.Invoke(this, e);

    private void OnImagePaneContextMenuRequested(ImagePaneView sender, BrowserPaneContextMenuRequestedEventArgs e) =>
        BrowserPaneContextMenuRequested?.Invoke(this, e);

    public FolderTreeView FolderTree => BrowseFolderTree;

    public ImagePaneView ImagePane => BrowseImagePane;

    public ContentControl FolderListHeaderHost => Browse2FolderListHeaderHost;

    public ContentControl FileListHeaderHost => Browse2FileListHeaderHost;

    /// <summary>Invoked on splitter release with normalized star weights (sum ≈ 1).</summary>
    public event Action<double, double>? FolderImagePaneSharesChanged;

    /// <summary>Forwarded from the file list header row (Name natural sort).</summary>
    public event RoutedEventHandler? FileListHeaderSortNameNatural;

    /// <summary>Forwarded from the file list header row (Size sort).</summary>
    public event RoutedEventHandler? FileListHeaderSortSize;

    /// <summary>Forwarded from the file list header row (Date sort).</summary>
    public event RoutedEventHandler? FileListHeaderSortDate;

    /// <summary>Forwarded from the folder list header row (Name natural).</summary>
    public event RoutedEventHandler? FolderListHeaderSortName;

    /// <summary>Forwarded from the folder list header row (Aggregate size).</summary>
    public event RoutedEventHandler? FolderListHeaderSortSize;

    /// <summary>Forwarded from the folder list header row (Image count).</summary>
    public event RoutedEventHandler? FolderListHeaderSortImageCount;

    /// <summary>Forwarded from the folder list header row (Date modified).</summary>
    public event RoutedEventHandler? FolderListHeaderSortDate;

    public void SetFolderHeaderRowVisible(bool visible) =>
        Browse2FolderListHeaderHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public void SetFileHeaderRowVisible(bool visible) =>
        Browse2FileListHeaderHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public void ApplyFolderImagePaneShares(double folderStar, double imageStar)
    {
        var f = Math.Max(1e-3, folderStar);
        var i = Math.Max(1e-3, imageStar);
        var sum = f + i;
        Browse2FolderColumn.Width = new GridLength(f / sum, GridUnitType.Star);
        Browse2ImageColumn.Width = new GridLength(i / sum, GridUnitType.Star);
    }

    private void Browse2FileHeaderSort_NameNatural_Click(object sender, RoutedEventArgs e) =>
        FileListHeaderSortNameNatural?.Invoke(sender, e);

    private void Browse2FileHeaderSort_Size_Click(object sender, RoutedEventArgs e) =>
        FileListHeaderSortSize?.Invoke(sender, e);

    private void Browse2FileHeaderSort_Date_Click(object sender, RoutedEventArgs e) =>
        FileListHeaderSortDate?.Invoke(sender, e);

    private void Browse2FolderHeaderSort_Name_Click(object sender, RoutedEventArgs e) =>
        FolderListHeaderSortName?.Invoke(sender, e);

    private void Browse2FolderHeaderSort_Size_Click(object sender, RoutedEventArgs e) =>
        FolderListHeaderSortSize?.Invoke(sender, e);

    private void Browse2FolderHeaderSort_ImageCount_Click(object sender, RoutedEventArgs e) =>
        FolderListHeaderSortImageCount?.Invoke(sender, e);

    private void Browse2FolderHeaderSort_Date_Click(object sender, RoutedEventArgs e) =>
        FolderListHeaderSortDate?.Invoke(sender, e);

    private void FolderImageSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement el)
            return;
        _folderImageSplitDrag = true;
        _folderImageSplitPressX = e.GetCurrentPoint(HostRoot).Position.X;
        _folderImageInitFolderW = Browse2FolderColumn.ActualWidth;
        _folderImageInitImageW = Browse2ImageColumn.ActualWidth;
        el.CapturePointer(e.Pointer);
    }

    private void FolderImageSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_folderImageSplitDrag || !e.Pointer.IsInContact)
            return;
        var x = e.GetCurrentPoint(HostRoot).Position.X;
        ApplyFolderImageResize(x - _folderImageSplitPressX);
    }

    private void FolderImageSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_folderImageSplitDrag)
            return;
        (sender as UIElement)?.ReleasePointerCapture(e.Pointer);
        EndFolderImageSplitDrag();
    }

    private void FolderImageSplitter_PointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        EndFolderImageSplitDrag();

    private void EndFolderImageSplitDrag()
    {
        if (!_folderImageSplitDrag)
            return;
        _folderImageSplitDrag = false;
        var f = Browse2FolderColumn.Width.Value;
        var i = Browse2ImageColumn.Width.Value;
        var sum = f + i;
        if (sum > 1e-6)
            FolderImagePaneSharesChanged?.Invoke(f / sum, i / sum);
    }

    private void ApplyFolderImageResize(double delta)
    {
        var newFolder = _folderImageInitFolderW + delta;
        var newImage = _folderImageInitImageW - delta;
        const double minPx = 80;
        if (newFolder < minPx)
        {
            newFolder = minPx;
            newImage = _folderImageInitFolderW + _folderImageInitImageW - newFolder;
        }
        else if (newImage < minPx)
        {
            newImage = minPx;
            newFolder = _folderImageInitFolderW + _folderImageInitImageW - newImage;
        }

        var sum = newFolder + newImage;
        if (sum < 1e-6)
            return;
        Browse2FolderColumn.Width = new GridLength(newFolder / sum, GridUnitType.Star);
        Browse2ImageColumn.Width = new GridLength(newImage / sum, GridUnitType.Star);
    }
}
