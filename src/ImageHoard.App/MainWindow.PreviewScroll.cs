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
    private uint _previewOrientedPixelWidth;
    private uint _previewOrientedPixelHeight;
    private double _previewUserZoomFactor = 1.0;
    private string? _previewZoomCommittedImagePath;
    private bool _previewPanActive;
    private uint _previewPanPointerId;
    private Point _previewPanLastRoot;
    private PointerEventHandler? _pointerMovedCaptureHandler;
    private PointerEventHandler? _pointerReleasedCaptureHandler;
    private PointerEventHandler? _pointerCaptureLostCaptureHandler;

    private enum ZoomScrollHostKind
    {
        Preview,
        Fullscreen,
    }

    private struct PendingZoomScrollAnchor
    {
        public ZoomScrollHostKind Host;
        public double ViewportPx;
        public double ViewportPy;
        public double U;
        public double V;
    }

    private PendingZoomScrollAnchor? _pendingZoomScrollAnchor;
    private bool _suppressNextZoomAnchorCenterCapture;
    private ScrollViewer? _previewPanCaptureScrollViewer;

    private void ClearPendingZoomScrollAnchor() => _pendingZoomScrollAnchor = null;

    private void RememberPreviewBitmapPixelSize(int pixelWidth, int pixelHeight)
    {
        _previewDecodedPixelWidth = Math.Max(0, pixelWidth);
        _previewDecodedPixelHeight = Math.Max(0, pixelHeight);
    }

    private void ClearPreviewBitmapPixelSize()
    {
        _previewDecodedPixelWidth = 0;
        _previewDecodedPixelHeight = 0;
        _previewOrientedPixelWidth = 0;
        _previewOrientedPixelHeight = 0;
    }

    private void RememberPreviewOrientedPixelSize(uint width, uint height)
    {
        _previewOrientedPixelWidth = width;
        _previewOrientedPixelHeight = height;
    }

    /// <summary>
    /// Logical DIP intrinsic size for layout: oriented file pixels for fit modes, decoded pixels for 1:1 (decode may be max-edge capped).
    /// </summary>
    private bool TryGetPreviewImageIntrinsicDips(double rasterizationScale, out double imgDipW, out double imgDipH)
    {
        if (_fitMode == ImageFitMode.OneToOne)
        {
            if (_previewDecodedPixelWidth <= 0 || _previewDecodedPixelHeight <= 0)
            {
                imgDipW = 0;
                imgDipH = 0;
                return false;
            }

            imgDipW = _previewDecodedPixelWidth / rasterizationScale;
            imgDipH = _previewDecodedPixelHeight / rasterizationScale;
            return true;
        }

        if (_previewOrientedPixelWidth > 0 && _previewOrientedPixelHeight > 0)
        {
            imgDipW = _previewOrientedPixelWidth / rasterizationScale;
            imgDipH = _previewOrientedPixelHeight / rasterizationScale;
            return true;
        }

        if (_previewDecodedPixelWidth > 0 && _previewDecodedPixelHeight > 0)
        {
            imgDipW = _previewDecodedPixelWidth / rasterizationScale;
            imgDipH = _previewDecodedPixelHeight / rasterizationScale;
            return true;
        }

        imgDipW = 0;
        imgDipH = 0;
        return false;
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
            ClearPendingZoomScrollAnchor();
            return;
        }

        if (!string.Equals(path, _previewZoomCommittedImagePath, StringComparison.OrdinalIgnoreCase))
        {
            ResetPreviewUserZoom();
            _previewZoomCommittedImagePath = path;
            ClearPendingZoomScrollAnchor();
        }
    }

    private bool HasDecodedPreviewForZoom() =>
        PreviewImage.Source is not null && _previewDecodedPixelWidth > 0 && _previewDecodedPixelHeight > 0;

    private bool TryExecuteViewZoomIn()
    {
        if (!HasDecodedPreviewForZoom())
            return false;
        var path = _currentImageFullPath;
        if (string.IsNullOrEmpty(path))
            return false;
        BeginZoomCommandScrollAnchorCapture();
        _previewUserZoomFactor = Math.Clamp(_previewUserZoomFactor * PreviewZoomStepRatio, PreviewZoomMinFactor, PreviewZoomMaxFactor);
        _ = ReloadPreviewAfterZoomChangeAsync(path);
        return true;
    }

    private bool TryExecuteViewZoomOut()
    {
        if (!HasDecodedPreviewForZoom())
            return false;
        var path = _currentImageFullPath;
        if (string.IsNullOrEmpty(path))
            return false;
        BeginZoomCommandScrollAnchorCapture();
        _previewUserZoomFactor = Math.Clamp(_previewUserZoomFactor / PreviewZoomStepRatio, PreviewZoomMinFactor, PreviewZoomMaxFactor);
        _ = ReloadPreviewAfterZoomChangeAsync(path);
        return true;
    }

    private bool TryExecuteViewZoomResetFit()
    {
        if (!HasDecodedPreviewForZoom())
            return false;
        var path = _currentImageFullPath;
        if (string.IsNullOrEmpty(path))
            return false;
        BeginZoomCommandScrollAnchorCapture();
        ResetPreviewUserZoom();
        _ = ReloadPreviewAfterZoomChangeAsync(path);
        return true;
    }

    private bool TryExecuteViewZoomActualPixels()
    {
        if (!HasDecodedPreviewForZoom())
            return false;
        if (!TryGetPreviewViewportDips(out var vw, out var vh))
            return false;
        var scale = (double)(RootGrid.XamlRoot?.RasterizationScale ?? 1.0);
        if (!TryGetPreviewImageIntrinsicDips(scale, out var imgDipW, out var imgDipH))
            return false;
        ComputeImageDisplayBaselineDipsWindowed(vw, vh, imgDipW, imgDipH, out var baseW, out var baseH);
        if (baseW < 1e-6 || baseH < 1e-6)
            return false;
        BeginZoomCommandScrollAnchorCapture();
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

    private void BeginZoomCommandScrollAnchorCapture()
    {
        if (!_suppressNextZoomAnchorCenterCapture)
            CaptureZoomAnchorViewportCenterForActiveHost();
        _suppressNextZoomAnchorCenterCapture = false;
    }

    /// <summary>Returns true when a wheel-driven pointer anchor was stored (suppresses viewport-center anchor).</summary>
    private bool TryPrepareZoomAnchorFromWheelIfNeeded(PointerRoutedEventArgs e, string commandId)
    {
        if (commandId is not (ViewZoomInCommandId or ViewZoomOutCommandId or ViewZoomResetFitCommandId))
            return false;
        var origin = e.OriginalSource as DependencyObject;
        if (!IsPointerChordAllowedForCommand(commandId, origin))
            return false;
        if (!_isFullscreen && IsDescendantOf(origin, PreviewHostGrid))
        {
            var pt = e.GetCurrentPoint(PreviewScrollViewer).Position;
            TryCaptureZoomAnchorFromViewportPoint(ZoomScrollHostKind.Preview, PreviewScrollViewer, pt.X, pt.Y);
            return true;
        }

        if (_isFullscreen && origin != null && IsDescendantOf(origin, FullscreenScrollContentGrid))
        {
            var pt = e.GetCurrentPoint(FullscreenScrollViewer).Position;
            TryCaptureZoomAnchorFromViewportPoint(ZoomScrollHostKind.Fullscreen, FullscreenScrollViewer, pt.X, pt.Y);
            return true;
        }

        return false;
    }

    private void CaptureZoomAnchorViewportCenterForActiveHost()
    {
        if (!HasDecodedPreviewForZoom())
            return;
        if (_isFullscreen)
        {
            if (FullscreenScrollViewer.ViewportWidth > 1 && FullscreenScrollViewer.ViewportHeight > 1)
            {
                TryCaptureZoomAnchorFromViewportPoint(
                    ZoomScrollHostKind.Fullscreen,
                    FullscreenScrollViewer,
                    FullscreenScrollViewer.ViewportWidth * 0.5,
                    FullscreenScrollViewer.ViewportHeight * 0.5);
            }
        }
        else if (PreviewScrollViewer.ViewportWidth > 1 && PreviewScrollViewer.ViewportHeight > 1)
        {
            TryCaptureZoomAnchorFromViewportPoint(
                ZoomScrollHostKind.Preview,
                PreviewScrollViewer,
                PreviewScrollViewer.ViewportWidth * 0.5,
                PreviewScrollViewer.ViewportHeight * 0.5);
        }
    }

    private void TryCaptureZoomAnchorFromViewportPoint(ZoomScrollHostKind host, ScrollViewer sv, double viewportPx, double viewportPy)
    {
        if (!HasDecodedPreviewForZoom())
            return;
        var z = Math.Clamp(_previewUserZoomFactor, PreviewZoomMinFactor, PreviewZoomMaxFactor);
        if (!TryComputeZoomScrollLayout(host, sv, z, out _, out _, out var dispW, out var dispH, out _, out _, out var imgLeft, out var imgTop))
            return;
        if (dispW < 1e-6 || dispH < 1e-6)
            return;
        var cx = sv.HorizontalOffset + viewportPx;
        var cy = sv.VerticalOffset + viewportPy;
        var u = (cx - imgLeft) / dispW;
        var v = (cy - imgTop) / dispH;
        _pendingZoomScrollAnchor = new PendingZoomScrollAnchor
        {
            Host = host,
            ViewportPx = viewportPx,
            ViewportPy = viewportPy,
            U = Math.Clamp(u, 0, 1),
            V = Math.Clamp(v, 0, 1),
        };
    }

    private bool TryComputeZoomScrollLayout(
        ZoomScrollHostKind host,
        ScrollViewer sv,
        double zoomZ,
        out double vw,
        out double vh,
        out double dispW,
        out double dispH,
        out double contentW,
        out double contentH,
        out double imgLeft,
        out double imgTop)
    {
        vw = sv.ViewportWidth;
        vh = sv.ViewportHeight;
        dispW = dispH = contentW = contentH = imgLeft = imgTop = 0;
        if (vw <= 1 || vh <= 1)
            return false;
        var scale = (double)(RootGrid.XamlRoot?.RasterizationScale ?? 1.0);
        if (!TryGetPreviewImageIntrinsicDips(scale, out var imgDipW, out var imgDipH))
            return false;
        zoomZ = Math.Clamp(zoomZ, PreviewZoomMinFactor, PreviewZoomMaxFactor);
        double baseW, baseH;
        if (host == ZoomScrollHostKind.Preview)
            ComputeImageDisplayBaselineDipsWindowed(vw, vh, imgDipW, imgDipH, out baseW, out baseH);
        else
            ComputeImageDisplayBaselineDipsFullscreen(vw, vh, imgDipW, imgDipH, out baseW, out baseH);
        if (baseW < 1e-6 || baseH < 1e-6)
            return false;
        dispW = baseW * zoomZ;
        dispH = baseH * zoomZ;
        contentW = Math.Max(vw, dispW);
        contentH = Math.Max(vh, dispH);
        imgLeft = (contentW - dispW) * 0.5;
        imgTop = (contentH - dispH) * 0.5;
        return true;
    }

    /// <returns>True when a matching pending anchor existed and <see cref="ScrollViewer.ChangeView"/> was applied.</returns>
    private bool TryApplyPendingZoomScrollAnchorForHost(ZoomScrollHostKind host, ScrollViewer sv)
    {
        if (_pendingZoomScrollAnchor is not { } a || a.Host != host)
            return false;
        var z = Math.Clamp(_previewUserZoomFactor, PreviewZoomMinFactor, PreviewZoomMaxFactor);
        if (!TryComputeZoomScrollLayout(host, sv, z, out var vw, out var vh, out var dispW, out var dispH, out var contentW, out var contentH, out var imgLeft, out var imgTop))
        {
            ClearPendingZoomScrollAnchor();
            return false;
        }

        var newH = imgLeft + a.U * dispW - a.ViewportPx;
        var newV = imgTop + a.V * dispH - a.ViewportPy;
        var maxH = Math.Max(0, contentW - vw);
        var maxV = Math.Max(0, contentH - vh);
        newH = Math.Clamp(newH, 0, maxH);
        newV = Math.Clamp(newV, 0, maxV);
        sv.ChangeView(newH, newV, null);
        ClearPendingZoomScrollAnchor();
        return true;
    }

    private static void ApplyDefaultFullscreenScrollCentering(ScrollViewer sv, double contentW, double contentH, double viewportW, double viewportH)
    {
        var maxH = Math.Max(0, contentW - viewportW);
        var maxV = Math.Max(0, contentH - viewportH);
        sv.ChangeView(maxH * 0.5, maxV * 0.5, null);
    }

    internal void PrepareFullscreenEnterNavigation()
    {
        FullscreenScrollViewer.ChangeView(0, 0, null);
        ClearPendingZoomScrollAnchor();
    }

    internal void PrepareFullscreenExitNavigation()
    {
        FullscreenScrollViewer.ChangeView(0, 0, null);
        FullscreenScrollContentGrid.Width = double.NaN;
        FullscreenScrollContentGrid.Height = double.NaN;
        ClearPendingZoomScrollAnchor();
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
        var w = FullscreenScrollViewer.ViewportWidth;
        var h = FullscreenScrollViewer.ViewportHeight;
        if (w > 1 && h > 1)
        {
            width = w;
            height = h;
            return true;
        }

        width = FullscreenLayout.ActualWidth;
        height = FullscreenLayout.ActualHeight;
        return width > 1 && height > 1;
    }

    /// <summary>
    /// Fullscreen image size applies <see cref="_previewUserZoomFactor"/> relative to the fit-mode baseline
    /// (<b>Shrink &amp; stretch</b>: uniform contain in the viewport, including compositor upscale when the intrinsic image is smaller
    /// than the viewport; <b>Shrink only</b>: same contain rule capped at oriented intrinsic logical size (no upscale at zoom 1); <b>1:1</b>: decoded DIP size).
    /// </summary>
    private void ApplyFullscreenImageForFitMode()
    {
        if (PreviewImage.Source is null || _previewDecodedPixelWidth <= 0 || _previewDecodedPixelHeight <= 0)
        {
            FullscreenImage.ClearValue(FrameworkElement.WidthProperty);
            FullscreenImage.ClearValue(FrameworkElement.HeightProperty);
            FullscreenImage.Stretch = Stretch.Uniform;
            FullscreenScrollContentGrid.Width = double.NaN;
            FullscreenScrollContentGrid.Height = double.NaN;
            FullscreenScrollViewer.ChangeView(0, 0, null);
            return;
        }

        var scale = (double)(RootGrid.XamlRoot?.RasterizationScale ?? 1.0);
        if (!TryGetPreviewImageIntrinsicDips(scale, out var imgDipW, out var imgDipH))
        {
            FullscreenImage.ClearValue(FrameworkElement.WidthProperty);
            FullscreenImage.ClearValue(FrameworkElement.HeightProperty);
            FullscreenImage.Stretch = Stretch.Uniform;
            FullscreenScrollContentGrid.Width = double.NaN;
            FullscreenScrollContentGrid.Height = double.NaN;
            FullscreenScrollViewer.ChangeView(0, 0, null);
            return;
        }

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

        var contentW = Math.Max(vw, dispW);
        var contentH = Math.Max(vh, dispH);
        FullscreenScrollContentGrid.Width = contentW;
        FullscreenScrollContentGrid.Height = contentH;

        var anchorApplied = TryApplyPendingZoomScrollAnchorForHost(ZoomScrollHostKind.Fullscreen, FullscreenScrollViewer);
        if (!anchorApplied
            && _isFullscreen
            && TryGetFullscreenViewportDips(out var fsVw, out var fsVh))
        {
            ApplyDefaultFullscreenScrollCentering(FullscreenScrollViewer, contentW, contentH, fsVw, fsVh);
        }
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
            ClearPendingZoomScrollAnchor();
            ApplyFullscreenImageForFitMode();
            return;
        }

        var scale = (double)(RootGrid.XamlRoot?.RasterizationScale ?? 1.0);
        if (!TryGetPreviewViewportDips(out var vw, out var vh))
        {
            ApplyFullscreenImageForFitMode();
            return;
        }

        if (!TryGetPreviewImageIntrinsicDips(scale, out var imgDipW, out var imgDipH))
        {
            ApplyFullscreenImageForFitMode();
            return;
        }

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
        _ = TryApplyPendingZoomScrollAnchorForHost(ZoomScrollHostKind.Preview, PreviewScrollViewer);
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
        if (origin is null)
            return false;
        ScrollViewer? panScroll = null;
        if (!_isFullscreen && IsDescendantOf(origin, PreviewHostGrid))
            panScroll = PreviewScrollViewer;
        else if (_isFullscreen && IsDescendantOf(origin, FullscreenScrollContentGrid))
            panScroll = FullscreenScrollViewer;
        if (panScroll is null)
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
            _previewPanCaptureScrollViewer = panScroll;
            panScroll.CapturePointer(e.Pointer);
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
        var sv = _previewPanCaptureScrollViewer ?? PreviewScrollViewer;
        sv.ChangeView(
            sv.HorizontalOffset - dx,
            sv.VerticalOffset - dy,
            null);
        e.Handled = true;
    }

    private void RootGrid_PreviewPanPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_previewPanActive || e.Pointer.PointerId != _previewPanPointerId)
            return;
        (_previewPanCaptureScrollViewer ?? PreviewScrollViewer).ReleasePointerCapture(e.Pointer);
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
        _previewPanCaptureScrollViewer = null;
    }
}
