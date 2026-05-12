using System.Runtime.InteropServices;

namespace ImageHoard.App.Native;

internal static class MonitorMetrics
{
    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
    }

    /// <summary>
    /// Physical pixel size of the monitor work area nearest the window (Win32), for decode sizing.
    /// </summary>
    public static bool TryGetNearestMonitorWorkAreaPx(IntPtr hwnd, out int width, out int height)
    {
        width = 1920;
        height = 1080;
        if (hwnd == IntPtr.Zero)
            return false;
        var hMonitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (hMonitor == IntPtr.Zero)
            return false;
        var mi = new MonitorInfo { cbSize = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(hMonitor, ref mi))
            return false;
        width = Math.Max(1, mi.rcWork.Right - mi.rcWork.Left);
        height = Math.Max(1, mi.rcWork.Bottom - mi.rcWork.Top);
        return true;
    }
}
