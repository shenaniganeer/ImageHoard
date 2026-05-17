using ImageHoard.Core.Browse2;

namespace ImageHoard.Tests;

/// <summary>Scroll anchoring contract (same math as <c>FolderTreeView</c>) with an in-memory row list instead of WinUI.</summary>
public sealed class TreeControllerScrollAnchorTests
{
    private sealed class FakeFolderTreeScrollViewport
    {
        public FakeFolderTreeScrollViewport(string indexRoot) => IndexRoot = indexRoot;

        public string IndexRoot { get; }

        public double VerticalOffset { get; set; }

        /// <summary>Small enough that <see cref="ScrollableHeight"/> is non-zero for short test lists (WinUI-like).</summary>
        public double ViewportHeight { get; set; } = 50;

        public double RowHeight { get; set; } = 32;

        public List<string> RowPaths { get; } = new();

        public double ScrollableHeight => Math.Max(0, RowPaths.Count * RowHeight - ViewportHeight);

        /// <summary>Mimics <c>FolderTreeView.ApplyModelDelta(..., preserveViewport: true)</c> after rows are patched.</summary>
        public void ApplyRowsAfterMutationPreserveViewport(Action patchRows)
        {
            var rowH = RowHeight;
            var cap = FolderTreeViewportAnchorMath.TryCaptureTopVisibleRow(
                VerticalOffset,
                rowH,
                RowPaths.Count,
                i => RowPaths[i]);
            patchRows();
            if (cap is null || RowPaths.Count == 0)
                return;
            VerticalOffset = FolderTreeViewportAnchorMath.ComputeRestoredVerticalOffset(
                cap.Value.AnchorPath,
                cap.Value.OffsetInRowPx,
                rowH,
                IndexRoot,
                RowPaths.Count,
                i => RowPaths[i],
                ScrollableHeight);
        }
    }

    [Fact]
    public void Insert_rows_above_preserves_intra_row_scroll_offset_of_top_visible_row()
    {
        var host = new FakeFolderTreeScrollViewport(@"C:\r");
        host.RowPaths.AddRange(new[] { @"C:\r", @"C:\r\a", @"C:\r\b" });
        host.VerticalOffset = 32 + 16;
        host.ApplyRowsAfterMutationPreserveViewport(() =>
        {
            host.RowPaths.Insert(0, @"C:\r\z");
        });

        Assert.Equal(2, FolderTreeViewportAnchorMath.ResolveRowIndexForAnchor(
            @"C:\r\a",
            host.IndexRoot,
            host.RowPaths.Count,
            i => host.RowPaths[i]));
        var maxScroll = Math.Max(0, host.RowPaths.Count * host.RowHeight - host.ViewportHeight);
        var expectedY = FolderTreeViewportAnchorMath.ClampVerticalScrollTarget(32 * 2 + 16, maxScroll);
        Assert.Equal(expectedY, host.VerticalOffset, 0.001);
    }

    [Fact]
    public void Deleted_anchor_row_restores_using_visible_ancestor()
    {
        var host = new FakeFolderTreeScrollViewport(@"C:\r");
        host.RowPaths.AddRange(new[] { @"C:\r", @"C:\r\a", @"C:\r\a\lost", @"C:\r\b" });
        host.VerticalOffset = 64;
        host.ApplyRowsAfterMutationPreserveViewport(() => host.RowPaths.RemoveAt(2));

        Assert.Equal(32, host.VerticalOffset, 0.001);
    }
}
