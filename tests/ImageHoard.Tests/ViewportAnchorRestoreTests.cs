using ImageHoard.Core.Browse;
using ImageHoard.Core.Browse2;

namespace ImageHoard.Tests;

public sealed class ViewportAnchorRestoreTests
{
    [Theory]
    [InlineData(10, 100, 10)]
    [InlineData(150, 100, 100)]
    [InlineData(-5, 80, 0)]
    public void ClampVerticalScrollTarget_matches_BrowserTreeSnapshot_clamp(double offset, double scrollable, double expected)
    {
        Assert.Equal(expected, BrowserTreeSnapshot.ClampScrollOffset(offset, scrollable), 5);
        Assert.Equal(expected, FolderTreeViewportAnchorMath.ClampVerticalScrollTarget(offset, scrollable), 5);
    }

    [Fact]
    public void ResolveRowIndexForAnchor_returns_direct_index_when_path_visible()
    {
        var paths = new[] { @"C:\r", @"C:\r\a", @"C:\r\b" };
        var ix = FolderTreeViewportAnchorMath.ResolveRowIndexForAnchor(
            @"c:\r\a",
            @"C:\r",
            paths.Length,
            i => paths[i]);
        Assert.Equal(1, ix);
    }

    [Fact]
    public void ResolveRowIndexForAnchor_missing_path_falls_back_to_nearest_existing_ancestor()
    {
        var paths = new[] { @"C:\r", @"C:\r\a" };
        var ix = FolderTreeViewportAnchorMath.ResolveRowIndexForAnchor(
            @"C:\r\a\gone\deep",
            @"C:\r",
            paths.Length,
            i => paths[i]);
        Assert.Equal(1, ix);
    }

    [Fact]
    public void ResolveRowIndexForAnchor_unknown_under_root_returns_zero()
    {
        var paths = new[] { @"D:\other", @"D:\other\z" };
        var ix = FolderTreeViewportAnchorMath.ResolveRowIndexForAnchor(
            @"C:\r\only",
            @"C:\r",
            paths.Length,
            i => paths[i]);
        Assert.Equal(0, ix);
    }

    [Fact]
    public void ComputeRestoredVerticalOffset_clamps_to_max_scroll()
    {
        var paths = new[] { @"C:\r", @"C:\r\a" };
        var y = FolderTreeViewportAnchorMath.ComputeRestoredVerticalOffset(
            @"C:\r\a",
            10,
            32,
            @"C:\r",
            paths.Length,
            i => paths[i],
            maxScrollOffset: 5);
        Assert.Equal(5, y);
    }

    [Fact]
    public void TryCaptureTopVisibleRow_clamps_offset_within_row_height()
    {
        var paths = new[] { @"C:\r", @"C:\r\a" };
        var cap = FolderTreeViewportAnchorMath.TryCaptureTopVisibleRow(9999, 32, paths.Length, i => paths[i]);
        Assert.NotNull(cap);
        Assert.Equal(@"C:\r\a", cap!.Value.AnchorPath, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(32, cap.Value.OffsetInRowPx);
    }
}
