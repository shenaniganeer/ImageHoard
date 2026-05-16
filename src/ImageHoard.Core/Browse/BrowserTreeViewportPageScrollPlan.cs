namespace ImageHoard.Core.Browse;

/// <summary>Outcome of <see cref="BrowserTreeViewportPageScrollPlan.Compute"/>.</summary>
public enum BrowserTreeViewportPageScrollResult
{
    /// <summary>Target fits fully inside the viewport; caller should not change vertical offset.</summary>
    TargetVisible,

    /// <summary>Target extends above the viewport; caller should scroll up by whole pages.</summary>
    TargetAboveViewport,

    /// <summary>Target extends below the viewport; caller should scroll down by whole pages.</summary>
    TargetBelowViewport,
}

/// <summary>Vertical scroll decision for page-on-exit tree viewport behavior.</summary>
public readonly record struct BrowserTreeViewportPageScrollDecision(
    BrowserTreeViewportPageScrollResult Result,
    double NewVerticalOffset);

/// <summary>
/// Pure geometry for page-sized folder-tree scrolling when sequential image navigation leaves the selection outside the viewport.
/// </summary>
public static class BrowserTreeViewportPageScrollPlan
{
    /// <summary>
    /// All coordinates are in the scrollable content coordinate system (same units as <c>ScrollViewer.VerticalOffset</c>).
    /// </summary>
    /// <param name="viewportTop">Current vertical scroll offset.</param>
    /// <param name="viewportHeight">Visible viewport height (must be positive for non-degenerate scrolling).</param>
    /// <param name="targetTop">Top edge of the target row in scroll coordinates.</param>
    /// <param name="targetHeight">Height of the target row.</param>
    /// <param name="scrollableHeight">Maximum vertical offset (e.g. <c>ScrollViewer.ScrollableHeight</c>).</param>
    public static BrowserTreeViewportPageScrollDecision Compute(
        double viewportTop,
        double viewportHeight,
        double targetTop,
        double targetHeight,
        double scrollableHeight)
    {
        if (viewportHeight <= 0
            || double.IsNaN(viewportHeight)
            || double.IsInfinity(viewportHeight)
            || targetHeight > viewportHeight)
        {
            return new BrowserTreeViewportPageScrollDecision(BrowserTreeViewportPageScrollResult.TargetVisible, viewportTop);
        }

        var viewportBottom = viewportTop + viewportHeight;
        var targetBottom = targetTop + targetHeight;

        if (targetTop >= viewportTop && targetBottom <= viewportBottom)
            return new BrowserTreeViewportPageScrollDecision(BrowserTreeViewportPageScrollResult.TargetVisible, viewportTop);

        var maxOffset = Math.Max(0, scrollableHeight);

        if (targetBottom > viewportBottom)
        {
            var overflowBelow = targetBottom - viewportBottom;
            var pages = (int)Math.Ceiling(overflowBelow / viewportHeight);
            var newOffset = viewportTop + pages * viewportHeight;
            newOffset = Math.Clamp(newOffset, 0, maxOffset);
            return new BrowserTreeViewportPageScrollDecision(BrowserTreeViewportPageScrollResult.TargetBelowViewport, newOffset);
        }

        // targetTop < viewportTop (partially or fully above)
        var overflowAbove = viewportTop - targetTop;
        var pagesUp = (int)Math.Ceiling(overflowAbove / viewportHeight);
        var newOffsetUp = viewportTop - pagesUp * viewportHeight;
        newOffsetUp = Math.Clamp(newOffsetUp, 0, maxOffset);
        return new BrowserTreeViewportPageScrollDecision(BrowserTreeViewportPageScrollResult.TargetAboveViewport, newOffsetUp);
    }
}
