using ImageHoard.App.Imaging;
using ImageHoard.App.Native;
using ImageHoard.Core.Imaging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT.Interop;

namespace ImageHoard.App;

public sealed partial class MainWindow
{
    private DispatcherQueueTimer? _previewResizeDebounceTimer;
    private int _lastDecodeTargetBoxMaxSidePx = -1;

    private void PreviewHostGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentImageFullPath))
            return;
        if (_fitMode == ImageFitMode.OneToOne)
            return;
        EnsurePreviewResizeDebounceTimer();
        if (_previewResizeDebounceTimer == null)
            return;
        _previewResizeDebounceTimer.Stop();
        _previewResizeDebounceTimer.Start();
    }

    private void EnsurePreviewResizeDebounceTimer()
    {
        if (_previewResizeDebounceTimer != null)
            return;
        var dq = DispatcherQueue.GetForCurrentThread();
        if (dq == null)
            return;
        _previewResizeDebounceTimer = dq.CreateTimer();
        _previewResizeDebounceTimer.Interval = TimeSpan.FromMilliseconds(280);
        _previewResizeDebounceTimer.Tick += (_, _) =>
        {
            _previewResizeDebounceTimer!.Stop();
            _ = ReloadIfDecodeTargetGrewAsync();
        };
    }

    private async Task ReloadIfDecodeTargetGrewAsync()
    {
        if (string.IsNullOrEmpty(_currentImageFullPath))
            return;
        if (ImageList.SelectedItem is not ImageRow row
            || !string.Equals(row.FullPath, _currentImageFullPath, StringComparison.OrdinalIgnoreCase))
            return;

        var layout = CreateWicDecodeLayout();
        var newMax = Math.Max(layout.TargetBoxWidthPx, layout.TargetBoxHeightPx);
        if (_lastDecodeTargetBoxMaxSidePx >= 0 && newMax <= _lastDecodeTargetBoxMaxSidePx * 1.10)
            return;

        var bmp = await WicBitmapLoader.DecodeWithOrientationAsync(_currentImageFullPath, layout);
        if (bmp == null)
            return;
        try
        {
            var src = new SoftwareBitmapSource();
            await src.SetBitmapAsync(bmp);
            PreviewImage.Source = src;
            FullscreenImage.Source = src;
            RememberDecodeTargetBox(layout);
        }
        catch
        {
            // keep previous bitmap
        }
    }

    private WicDecodeLayout CreateWicDecodeLayout()
    {
        var maxEdge = ResolveMaxDecodeEdgePx();
        var scale = (double)(RootGrid.XamlRoot?.RasterizationScale ?? 1.0);
        var previewPxW = DipToPhysicalPx(PreviewImage.ActualWidth, scale);
        var previewPxH = DipToPhysicalPx(PreviewImage.ActualHeight, scale);
        TryGetPrimaryWorkAreaPx(out var workW, out var workH);
        var targetW = Math.Max(previewPxW, workW);
        var targetH = Math.Max(previewPxH, workH);

        if (_fitMode == ImageFitMode.OneToOne)
            return new WicDecodeLayout(int.MaxValue, int.MaxValue, BitmapUniformKind.FullResolution, maxEdge);

        var kind = _fitMode == ImageFitMode.Fill ? BitmapUniformKind.Fill : BitmapUniformKind.Fit;
        return new WicDecodeLayout(targetW, targetH, kind, maxEdge);
    }

    private void RememberDecodeTargetBox(in WicDecodeLayout layout)
    {
        _lastDecodeTargetBoxMaxSidePx = layout.UniformKind == BitmapUniformKind.FullResolution
            ? -1
            : Math.Max(layout.TargetBoxWidthPx, layout.TargetBoxHeightPx);
    }

    private int ResolveMaxDecodeEdgePx()
    {
        if (!TryGetPrimaryWorkAreaPx(out var ww, out var wh))
            return 8192;
        var m = Math.Max(ww, wh);
        return Math.Clamp(m * 2, 4096, 8192);
    }

    private bool TryGetPrimaryWorkAreaPx(out int width, out int height)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            return MonitorMetrics.TryGetNearestMonitorWorkAreaPx(hwnd, out width, out height);
        }
        catch
        {
            width = 1920;
            height = 1080;
            return false;
        }
    }

    private static int DipToPhysicalPx(double dip, double rasterizationScale) =>
        dip <= 0 ? 0 : Math.Max(1, (int)Math.Round(dip * rasterizationScale));
}
