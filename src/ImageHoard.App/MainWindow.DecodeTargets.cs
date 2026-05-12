using ImageHoard.App.Imaging;
using ImageHoard.App.Native;
using ImageHoard.Core.Imaging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.Threading;
using WinRT.Interop;

namespace ImageHoard.App;

public sealed partial class MainWindow
{
    private const int PreviewResizeDebounceMs = 280;

    private DispatcherQueueTimer? _previewResizeDebounceTimer;
    private int _lastDecodeTargetBoxWidthPx = -1;
    private int _lastDecodeTargetBoxHeightPx = -1;

    private void PreviewHostGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePreviewScrollMetrics();
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
        _previewResizeDebounceTimer.Interval = TimeSpan.FromMilliseconds(PreviewResizeDebounceMs);
        _previewResizeDebounceTimer.Tick += (_, _) =>
        {
            _previewResizeDebounceTimer!.Stop();
            _ = ReloadIfDecodeTargetBoxChangedAsync();
        };
    }

    /// <summary>Clears remembered decode target so the next reload runs unconditionally (e.g. fullscreen toggle).</summary>
    private void InvalidateDecodeTargetTracking()
    {
        _lastDecodeTargetBoxWidthPx = -1;
        _lastDecodeTargetBoxHeightPx = -1;
    }

    private async Task ReloadIfDecodeTargetBoxChangedAsync()
    {
        if (string.IsNullOrEmpty(_currentImageFullPath))
            return;
        if (IsPreviewNavigationQueueNonEmpty())
            return;
        if (GetSelectedImageRow() is not { } row
            || !string.Equals(row.FullPath, _currentImageFullPath, StringComparison.OrdinalIgnoreCase))
            return;

        var layout = CreateWicDecodeLayout();
        if (_lastDecodeTargetBoxWidthPx >= 0
            && _lastDecodeTargetBoxHeightPx >= 0
            && !DecodeTargetBoxMeaningfullyChanged(
                _lastDecodeTargetBoxWidthPx,
                _lastDecodeTargetBoxHeightPx,
                layout.TargetBoxWidthPx,
                layout.TargetBoxHeightPx))
            return;

        var path = _currentImageFullPath;
        Interlocked.Increment(ref _previewInvalidateGeneration);
        await DecodeAndCommitPreviewAsync(path).ConfigureAwait(true);
    }

    /// <summary>True when either axis differs by at least 10% from the last decode (grow or shrink).</summary>
    private static bool DecodeTargetBoxMeaningfullyChanged(int oldW, int oldH, int newW, int newH)
    {
        if (oldW <= 0 || oldH <= 0)
            return true;
        var dw = Math.Abs(newW - (double)oldW) / oldW;
        var dh = Math.Abs(newH - (double)oldH) / oldH;
        return dw >= 0.10 || dh >= 0.10;
    }

    private WicDecodeLayout CreateWicDecodeLayout()
    {
        var maxEdge = ResolveMaxDecodeEdgePx();
        var scale = (double)(RootGrid.XamlRoot?.RasterizationScale ?? 1.0);
        if (!TryGetPreviewViewportDips(out var dipW, out var dipH))
        {
            dipW = PreviewHostGrid.ActualWidth;
            dipH = PreviewHostGrid.ActualHeight;
        }

        var previewPxW = DipToPhysicalPx(dipW, scale);
        var previewPxH = DipToPhysicalPx(dipH, scale);
        TryGetPrimaryWorkAreaPx(out var workW, out var workH);

        int targetW;
        int targetH;
        if (_isFullscreen)
        {
            targetW = Math.Max(1, workW);
            targetH = Math.Max(1, workH);
        }
        else
        {
            targetW = Math.Max(1, previewPxW);
            targetH = Math.Max(1, previewPxH);
        }

        if (_fitMode == ImageFitMode.OneToOne)
            return new WicDecodeLayout(int.MaxValue, int.MaxValue, BitmapUniformKind.FullResolution, maxEdge);

        var kind = _fitMode == ImageFitMode.Fill ? BitmapUniformKind.Fill : BitmapUniformKind.Fit;
        return new WicDecodeLayout(targetW, targetH, kind, maxEdge);
    }

    private void RememberDecodeTargetBox(in WicDecodeLayout layout)
    {
        if (layout.UniformKind == BitmapUniformKind.FullResolution)
        {
            _lastDecodeTargetBoxWidthPx = -1;
            _lastDecodeTargetBoxHeightPx = -1;
        }
        else
        {
            _lastDecodeTargetBoxWidthPx = layout.TargetBoxWidthPx;
            _lastDecodeTargetBoxHeightPx = layout.TargetBoxHeightPx;
        }
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
