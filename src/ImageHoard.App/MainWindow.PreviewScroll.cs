using ImageHoard.Core.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace ImageHoard.App;

public sealed partial class MainWindow
{
    internal const string ViewPanPreviewCommandId = "view.panPreview";

    private int _previewDecodedPixelWidth;
    private int _previewDecodedPixelHeight;
    private bool _previewPanActive;
    private uint _previewPanPointerId;
    private Point _previewPanLastRoot;
    private PointerEventHandler? _pointerMovedCaptureHandler;
    private PointerEventHandler? _pointerReleasedCaptureHandler;
    private PointerEventHandler? _pointerCaptureLostCaptureHandler;

    private void RememberPreviewBitmapPixelSize(int pixelWidth, int pixelHeight)
    {
        _previewDecodedPixelWidth = Math.Max(0, pixelWidth);
        _previewDecodedPixelHeight = Math.Max(0, pixelHeight);
    }

    private void ClearPreviewBitmapPixelSize()
    {
        _previewDecodedPixelWidth = 0;
        _previewDecodedPixelHeight = 0;
    }

    /// <summary>Viewport in DIPs for WIC decode target (avoids using <see cref="PreviewImage"/> measure inside <see cref="ScrollViewer"/>).</summary>
    private bool TryGetPreviewViewportDips(out double width, out double height)
    {
        width = PreviewScrollViewer.ViewportWidth;
        height = PreviewScrollViewer.ViewportHeight;
        if (width > 1 && height > 1)
            return true;
        width = PreviewHostGrid.ActualWidth;
        height = PreviewHostGrid.ActualHeight;
        return width > 1 && height > 1;
    }

    private void UpdatePreviewScrollMetrics()
    {
        if (PreviewImage.Source is null || _previewDecodedPixelWidth <= 0 || _previewDecodedPixelHeight <= 0)
        {
            PreviewScrollContentGrid.Width = double.NaN;
            PreviewScrollContentGrid.Height = double.NaN;
            PreviewImage.ClearValue(FrameworkElement.WidthProperty);
            PreviewImage.ClearValue(FrameworkElement.HeightProperty);
            PreviewImage.Stretch = Stretch.Uniform;
            PreviewScrollViewer.ChangeView(0, 0, null);
            return;
        }

        var scale = (double)(RootGrid.XamlRoot?.RasterizationScale ?? 1.0);
        if (!TryGetPreviewViewportDips(out var vw, out var vh))
            return;

        var imgDipW = _previewDecodedPixelWidth / scale;
        var imgDipH = _previewDecodedPixelHeight / scale;
        var contentW = Math.Max(vw, imgDipW);
        var contentH = Math.Max(vh, imgDipH);
        PreviewScrollContentGrid.Width = contentW;
        PreviewScrollContentGrid.Height = contentH;
        PreviewImage.Width = imgDipW;
        PreviewImage.Height = imgDipH;
        PreviewImage.Stretch = Stretch.None;
        PreviewImage.HorizontalAlignment = HorizontalAlignment.Center;
        PreviewImage.VerticalAlignment = VerticalAlignment.Center;
    }

    private void RegisterPreviewPanPointerHandlers()
    {
        _pointerMovedCaptureHandler ??= (_, e) => RootGrid_PreviewPanPointerMoved(_, e);
        _pointerReleasedCaptureHandler ??= (_, e) => RootGrid_PreviewPanPointerReleased(_, e);
        _pointerCaptureLostCaptureHandler ??= (_, e) => RootGrid_PreviewPanPointerCaptureLost(_, e);
        RootGrid.AddHandler(UIElement.PointerMovedEvent, _pointerMovedCaptureHandler, handledEventsToo: true);
        RootGrid.AddHandler(UIElement.PointerReleasedEvent, _pointerReleasedCaptureHandler, handledEventsToo: true);
        RootGrid.AddHandler(UIElement.PointerCaptureLostEvent, _pointerCaptureLostCaptureHandler, handledEventsToo: true);
    }

    private bool TryBeginPreviewPan(
        PointerRoutedEventArgs e,
        InputProfileDocument merged,
        string buttonName,
        bool shift,
        bool ctrl,
        bool alt,
        bool win,
        DependencyObject? origin)
    {
        if (_splitDrag != SplitDragKind.None)
            return false;
        if (origin is null || !IsDescendantOf(origin, PreviewHostGrid))
            return false;
        if (merged.Bindings is null
            || !merged.Bindings.TryGetValue(ViewPanPreviewCommandId, out var chords))
            return false;

        foreach (var chord in chords)
        {
            if (!InputPointerChordMatch.IsMouseButtonMatch(chord, buttonName, 1, shift, ctrl, alt, win))
                continue;
            if (!IsPointerChordAllowedForCommand(ViewPanPreviewCommandId, origin))
                continue;
            _previewPanActive = true;
            _previewPanPointerId = e.Pointer.PointerId;
            _previewPanLastRoot = e.GetCurrentPoint(null).Position;
            PreviewScrollViewer.CapturePointer(e.Pointer);
            e.Handled = true;
            return true;
        }

        return false;
    }

    private void RootGrid_PreviewPanPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_previewPanActive || e.Pointer.PointerId != _previewPanPointerId || !e.Pointer.IsInContact)
            return;
        var pos = e.GetCurrentPoint(null).Position;
        var dx = pos.X - _previewPanLastRoot.X;
        var dy = pos.Y - _previewPanLastRoot.Y;
        _previewPanLastRoot = pos;
        PreviewScrollViewer.ChangeView(
            PreviewScrollViewer.HorizontalOffset - dx,
            PreviewScrollViewer.VerticalOffset - dy,
            null);
        e.Handled = true;
    }

    private void RootGrid_PreviewPanPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_previewPanActive || e.Pointer.PointerId != _previewPanPointerId)
            return;
        PreviewScrollViewer.ReleasePointerCapture(e.Pointer);
        EndPreviewPan();
        e.Handled = true;
    }

    private void RootGrid_PreviewPanPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_previewPanActive || e.Pointer.PointerId != _previewPanPointerId)
            return;
        EndPreviewPan();
    }

    private void EndPreviewPan()
    {
        _previewPanActive = false;
    }
}
