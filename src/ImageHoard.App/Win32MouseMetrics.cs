using System.Runtime.InteropServices;
using ImageHoard.Core.Input;

namespace ImageHoard.App;

internal static class Win32MouseMetrics
{
    private const int SmCxDoubleClk = 36;
    private const int SmCyDoubleClk = 37;

    [DllImport("USER32.dll", ExactSpelling = true)]
    private static extern int GetDoubleClickTime();

    [DllImport("USER32.dll", ExactSpelling = true)]
    private static extern int GetSystemMetrics(int nIndex);

    /// <summary>Builds metrics for <see cref="MouseButtonClickChainTracker"/>; <paramref name="dipsPerPixel"/> is WinUI <c>XamlRoot.RasterizationScale</c> (physical pixels per DIP).</summary>
    /// <param name="doubleClickTimeMsOverride">When set, used as <see cref="MouseButtonClickMetrics.DoubleClickTimeMs"/> instead of <c>GetDoubleClickTime</c>.</param>
    public static MouseButtonClickMetrics GetClickMetrics(double dipsPerPixel, int? doubleClickTimeMsOverride = null)
    {
        var scale = dipsPerPixel > 0 ? dipsPerPixel : 1.0;
        var cxPx = Math.Max(0, GetSystemMetrics(SmCxDoubleClk));
        var cyPx = Math.Max(0, GetSystemMetrics(SmCyDoubleClk));
        var ms = doubleClickTimeMsOverride is { } o ? Math.Max(1, o) : GetDoubleClickTimeMs();
        return new MouseButtonClickMetrics(ms, cxPx / scale, cyPx / scale);
    }

    public static int GetDoubleClickTimeMs() => Math.Max(1, GetDoubleClickTime());
}
