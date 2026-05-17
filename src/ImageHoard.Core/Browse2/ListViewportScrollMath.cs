namespace ImageHoard.Core.Browse2;

/// <summary>
/// Shared vertical scroll math for virtualized lists (folder tree, image list): fully-visible test and pin item top at a viewport Y offset.
/// </summary>
public static class ListViewportScrollMath
{
    /// <summary>
    /// Computes a new vertical scroll offset to place the item's top edge
    /// <paramref name="pinItemTopAtViewportY"/> pixels below the viewport top, clamped to <c>[0, maxScrollOffset]</c>.
    /// Returns <see langword="null"/> when the caller should leave the scroll offset unchanged.
    /// </summary>
    public static double? TryComputePinnedVerticalOffset(
        double itemContentTop,
        double itemContentHeight,
        double viewportOffset,
        double viewportHeight,
        double maxScrollOffset,
        double pinItemTopAtViewportY,
        bool skipIfFullyVisible)
    {
        if (!IsFiniteNonNegative(itemContentHeight) || itemContentHeight <= 0)
            return null;
        if (!IsFinite(viewportOffset) || !IsFinite(viewportHeight) || viewportHeight <= 0)
            return null;
        if (!IsFinite(itemContentTop))
            return null;
        if (!IsFinite(maxScrollOffset) || maxScrollOffset < 0)
            return null;
        if (!IsFinite(pinItemTopAtViewportY) || pinItemTopAtViewportY < 0)
            return null;

        // Match BrowserTreeViewportPageScrollPlan: taller-than-viewport counts as "visible" (no scroll).
        if (itemContentHeight > viewportHeight)
            return null;

        var vt = viewportOffset;
        var vb = viewportOffset + viewportHeight;
        var tt = itemContentTop;
        var tb = itemContentTop + itemContentHeight;

        var fullyVisible = tt >= vt && tb <= vb;
        if (skipIfFullyVisible && fullyVisible)
            return null;

        var desired = itemContentTop - pinItemTopAtViewportY;
        var clamped = FolderTreeViewportAnchorMath.ClampVerticalScrollTarget(desired, maxScrollOffset);
        if (IsNearlyEqual(clamped, viewportOffset))
            return null;
        return clamped;
    }

    private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

    private static bool IsFiniteNonNegative(double v) => IsFinite(v) && v >= 0;

    private static bool IsNearlyEqual(double a, double b) => Math.Abs(a - b) < 0.5;
}
