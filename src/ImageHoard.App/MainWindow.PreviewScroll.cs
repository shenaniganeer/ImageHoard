using ImageHoard.App.Imaging;
using ImageHoard.Core.Imaging;
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
    internal const string ViewZoomInCommandId = "view.zoomIn";
    internal const string ViewZoomOutCommandId = "view.zoomOut";
    internal const string ViewZoomResetFitCommandId = "view.zoomResetFit";
    internal const string ViewZoomActualPixelsCommandId = "view.zoomActualPixels";

    private const double PreviewZoomMinFactor = 0.1;
    private const double PreviewZoomMaxFactor = 10.0;
    private const double PreviewZoomStepRatio = 1.1;

    private int _previewDecodedPixelWidth;
    private int _previewDecodedPixelHeight;
    private double _previewUserZoomFactor = 1.0;
    private string? _previewZoomCommittedImagePath;
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

    private void ResetPreviewUserZoom()
    {
        _previewUserZoomFactor = 1.0;
    }

    /// <summary>Resets zoom when the committed preview path changes or preview is cleared; call before updating metrics.</summary>
    private void OnPreviewImagePathCommitted(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            ResetPreviewUserZoom();
            _previewZoomCommittedImagePath = null;
            return;
        }

        if (!string.Equals(path, _previewZoomCommittedImagePath, StringComparison.OrdinalIgnoreCase))
        {
            ResetPreviewUserZoom();
            _previewZoomCommittedImagePath = path;
        }
    }

    private bool HasDecodedPreviewForZoom() =>
        PreviewImage.Source is not null && _previewDecodedPixelWidth > 0 && _previewDecodedPixelHeight > 0;

    private bool TryExecuteViewZoomIn()
    {
        if (!HasDecodedPreviewForZoom())
            return false;
        _previewUserZoomFactor = Math.Clamp(_previewUserZoomFactor * PreviewZoomStepRatio, PreviewZoomMinFactor, PreviewZoomMaxFactor);
        UpdatePreviewScrollMetrics();
        return true;
    }

    private bool TryExecuteViewZoomOut()
    {
        if (!HasDecodedPreviewForZoom())
            return false;
        _previewUserZoomFactor = Math.Clamp(_previewUserZoomFactor / PreviewZoomStepRatio, PreviewZoomMinFactor, PreviewZoomMaxFactor);
        UpdatePreviewScrollMetrics();
        return true;
    }

    private bool TryExecuteViewZoomResetFit()
    {
        if (!HasDecodedPreviewForZoom())
            return false;
        ResetPreviewUserZoom();
        UpdatePreviewScrollMetrics();
        return true;
    }

    private bool TryExecuteViewZoomActualPixels()
    {
        if (!HasDecodedPreviewForZoom())
            return false;
        if (!TryGetPreviewViewportDips(out var vw, out var vh))
            return false;
        var scale = (double)(RootGrid.XamlRoot?.RasterizationScale ?? 1.0);
        var imgDipW = _previewDecodedPixelWidth / scale;
        var imgDipH = _previewDecodedPixelHeight / scale;
        ComputeImageDisplayBaselineDipsWindowed(vw, vh, imgDipW, imgDipH, out var baseW, out var baseH);
        if (baseW < 1e-6 || baseH < 1e-6)
            return false;
        _previewUserZoomFactor = Math.Clamp(imgDipW / baseW, PreviewZoomMinFactor, PreviewZoomMaxFactor);
        UpdatePreviewScrollMetrics();
        return true;
    }

    /// <summary>Starts actual-pixels zoom; may re-decode at full resolution (max-edge cap) when the current bitmap is fit-downscaled.</summary>
    internal void RequestViewZoomActualPixelsAsync() => _ = ViewZoomActualPixelsCoreAsync();

    /// <summary>For hotkeys: returns false when no preview to zoom.</summary>
    internal bool TryRequestViewZoomActualPixelsFromInput()
    {
        if (!HasDecodedPreviewForZoom())
            return false;
        RequestViewZoomActualPixelsAsync();
        return true;
    }

    private async Task ViewZoomActualPixelsCoreAsync()
    {
        if (string.IsNullOrEmpty(_currentImageFullPath))
            return;
        if (!HasDecodedPreviewForZoom())
            return;

        var path = _currentImageFullPath;
        var (ow, oh) = await WicBitmapLoader.GetOrientedPixelDimensionsAsync(path).ConfigureAwait(true);
        if (ow == 0 || oh == 0)
        {
            TryExecuteViewZoomActualPixels();
            return;
        }

        var maxEdge = ResolveMaxDecodeEdgePx();
        var (capW, capH) = BitmapDecodeFit.ComputeOutputDimensions(
            ow,
            oh,
            int.MaxValue,
            int.MaxValue,
            maxEdge,
            BitmapUniformKind.FullResolution);

        var needsFullDecode =
            _fitMode != ImageFitMode.OneToOne
            && (_previewDecodedPixelWidth < (int)capW || _previewDecodedPixelHeight < (int)capH);

        if (needsFullDecode)
        {
            SetForceNextPreviewFullResolutionDecode();
            await CommitPreviewImmediatelyAsync(path).ConfigureAwait(true);
            if (!string.Equals(_currentImageFullPath, path, StringComparison.OrdinalIgnoreCase))
                return;
            if (!HasDecodedPreviewForZoom())
                return;
            ResetPreviewUserZoom();
            TryExecuteViewZoomActualPixels();
            return;
        }

        TryExecuteViewZoomActualPixels();
    }

    /// <summary>Baseline displayed size (DIPs) at zoom 1 for the windowed preview pane.</summary>
    private void ComputeImageDisplayBaselineDipsWindowed(
        double viewportW,
        double viewportH,
        double imgDipW,
        double imgDipH,
        out double baseW,
        out double baseH)
    {
        switch (_fitMode)
        {
            case ImageFitMode.OneToOne:
                baseW = imgDipW;
                baseH = imgDipH;
                break;
            case ImageFitMode.ShrinkAndStretch:
                {
                    var s = PreviewCoverLayout.UniformContainScale(viewportW, viewportH, imgDipW, imgDipH);
                    if (s <= 0)
                    {
                        baseW = imgDipW;
                        baseH = imgDipH;
                    }
                    else
                    {
                        baseW = imgDipW * s;
                        baseH = imgDipH * s;
                    }
                }
                break;
            case ImageFitMode.ShrinkOnly:
            default:
                {
                    var s = PreviewCoverLayout.UniformContainScaleShrinkOnly(viewportW, viewportH, imgDipW, imgDipH);
                    if (s <= 0)
                    {
                        baseW = imgDipW;
                        baseH = imgDipH;
                    }
                    else
                    {
                        baseW = imgDipW * s;
                        baseH = imgDipH * s;
                    }
                }
                break;
        }
    }

    /// <summary>Baseline displayed size (DIPs) at zoom 1 for fullscreen (uniform contain in viewport).</summary>
    private void ComputeImageDisplayBaselineDipsFullscreen(
        double viewportW,
        double viewportH,
        double imgDipW,
        double imgDipH,
        out double baseW,
        out double baseH)
    {
        switch (_fitMode)
        {
            case ImageFitMode.OneToOne:
                baseW = imgDipW;
                baseH = imgDipH;
                break;
            case ImageFitMode.ShrinkAndStretch:
                {
                    var s = PreviewCoverLayout.UniformContainScale(viewportW, viewportH, imgDipW, imgDipH);
                    if (s <= 0)
                    {
                        baseW = imgDipW;
                        baseH = imgDipH;
                    }
                    else
                    {
                        baseW = imgDipW * s;
                        baseH = imgDipH * s;
                    }
                }
                break;
            case ImageFitMode.ShrinkOnly:
            default:
                {
                    var s = PreviewCoverLayout.UniformContainScaleShrinkOnly(viewportW, viewportH, imgDipW, imgDipH);
                    if (s <= 0)
                    {
                        baseW = imgDipW;
                        baseH = imgDipH;
                    }
                    else
                    {
                        baseW = imgDipW * s;
                        baseH = imgDipH * s;
                    }
                }
                break;
        }
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

    private bool TryGetFullscreenViewportDips(out double width, out double height)
    {
        width = FullscreenLayout.ActualWidth;
        height = FullscreenLayout.ActualHeight;
        return width > 1 && height > 1;
    }

    /// <summary>
    /// Fullscreen image size applies <see cref="_previewUserZoomFactor"/> relative to the fit-mode baseline
    /// (<b>Shrink &amp; stretch</b>: uniform contain in the viewport, including compositor upscale when the image is smaller
    /// than the viewport; <b>Shrink only</b>: same contain rule capped at decoded DIP size (no upscale); <b>1:1</b>: decoded DIP size).
    /// </summary>
    private void ApplyFullscreenImageForFitMode()
    {
        if (PreviewImage.Source is null || _previewDecodedPixelWidth <= 0 || _previewDecodedPixelHeight <= 0)
        {
            FullscreenImage.ClearValue(FrameworkElement.WidthProperty);
            FullscreenImage.ClearValue(FrameworkElement.HeightProperty);
            FullscreenImage.Stretch = Stretch.Uniform;
            return;
        }

        var scale = (double)(RootGrid.XamlRoot?.RasterizationScale ?? 1.0);
        var imgDipW = _previewDecodedPixelWidth / scale;
        var imgDipH = _previewDecodedPixelHeight / scale;
        var z = _previewUserZoomFactor;

        if (!TryGetFullscreenViewportDips(out var vw, out var vh))
        {
            vw = imgDipW;
            vh = imgDipH;
        }

        ComputeImageDisplayBaselineDipsFullscreen(vw, vh, imgDipW, imgDipH, out var baseW, out var baseH);
        var dispW = baseW * z;
        var dispH = baseH * z;

        // Uniform scales the bitmap into Width/Height; None keeps intrinsic size and ignores the zoom box.
        FullscreenImage.Stretch = Stretch.Uniform;
        FullscreenImage.Width = dispW;
        FullscreenImage.Height = dispH;
        FullscreenImage.HorizontalAlignment = HorizontalAlignment.Center;
        FullscreenImage.VerticalAlignment = VerticalAlignment.Center;
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
            ResetPreviewUserZoom();
            ApplyFullscreenImageForFitMode();
            return;
        }

        var scale = (double)(RootGrid.XamlRoot?.RasterizationScale ?? 1.0);
        if (!TryGetPreviewViewportDips(out var vw, out var vh))
        {
            ApplyFullscreenImageForFitMode();
            return;
        }

        var imgDipW = _previewDecodedPixelWidth / scale;
        var imgDipH = _previewDecodedPixelHeight / scale;
        var z = _previewUserZoomFactor;

        ComputeImageDisplayBaselineDipsWindowed(vw, vh, imgDipW, imgDipH, out var baseW, out var baseH);
        var dispW = baseW * z;
        var dispH = baseH * z;
        var contentW = Math.Max(vw, dispW);
        var contentH = Math.Max(vh, dispH);
        PreviewScrollContentGrid.Width = contentW;
        PreviewScrollContentGrid.Height = contentH;
        PreviewImage.Width = dispW;
        PreviewImage.Height = dispH;
        // Uniform scales the bitmap into Width/Height; None keeps intrinsic size and ignores the zoom box.
        PreviewImage.Stretch = Stretch.Uniform;
        PreviewImage.HorizontalAlignment = HorizontalAlignment.Center;
        PreviewImage.VerticalAlignment = VerticalAlignment.Center;
        ApplyFullscreenImageForFitMode();
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
