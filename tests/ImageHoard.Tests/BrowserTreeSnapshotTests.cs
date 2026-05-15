using ImageHoard.Core.Browse;
using Xunit;

namespace ImageHoard.Tests;

public sealed class BrowserTreeSnapshotTests
{
    [Theory]
    [InlineData(@"C:\Photos", @"c:\photos", true)]
    [InlineData(@"C:\Photos\", @"C:\Photos", true)]
    [InlineData(@"C:\Photos", @"C:\Other", false)]
    [InlineData(null, @"C:\a", false)]
    public void IsRestoreRootMatching_normalizes_and_compares(string? snap, string? browse, bool expected) =>
        Assert.Equal(expected, BrowserTreeSnapshot.IsRestoreRootMatching(snap, browse));

    [Fact]
    public void SanitizeStoredScroll_accepts_non_negative_finite()
    {
        Assert.Equal(12.5, BrowserTreeSnapshot.SanitizeStoredScroll(12.5));
        Assert.Equal(0d, BrowserTreeSnapshot.SanitizeStoredScroll(0));
        Assert.Null(BrowserTreeSnapshot.SanitizeStoredScroll(-1));
        Assert.Null(BrowserTreeSnapshot.SanitizeStoredScroll(double.NaN));
        Assert.Null(BrowserTreeSnapshot.SanitizeStoredScroll(double.PositiveInfinity));
        Assert.Null(BrowserTreeSnapshot.SanitizeStoredScroll(null));
    }

    [Theory]
    [InlineData(10, 100, 10)]
    [InlineData(150, 100, 100)]
    [InlineData(-5, 80, 0)]
    public void ClampScrollOffset_clamps_to_scrollable(double offset, double scrollable, double expected) =>
        Assert.Equal(expected, BrowserTreeSnapshot.ClampScrollOffset(offset, scrollable));

    [Fact]
    public void MergePriorityThenCapDedupeUnderRoot_prioritizes_and_caps()
    {
        var root = @"C:\root";
        var priority = new[] { @"C:\root\a", @"C:\root\a\b" };
        var rest = Enumerable.Range(0, 100).Select(i => $@"C:\root\z{i}").ToArray();
        var merged = BrowserTreeSnapshot.MergePriorityThenCapDedupeUnderRoot(root, priority, rest, maxCount: 5);
        Assert.Equal(5, merged.Count);
        Assert.Equal(@"C:\root\a", merged[0]);
        Assert.Equal(@"C:\root\a\b", merged[1]);
        Assert.Contains(@"C:\root\z0", merged);
        Assert.DoesNotContain(@"C:\root\outside", merged);
    }

    [Fact]
    public void MergePriorityThenCapDedupeUnderRoot_dedupes_case_insensitively()
    {
        var root = @"C:\root";
        var merged = BrowserTreeSnapshot.MergePriorityThenCapDedupeUnderRoot(
            root,
            new[] { @"C:\root\Sub" },
            new[] { @"c:\root\sub" },
            maxCount: 10);
        Assert.Single(merged);
        Assert.Equal(@"C:\root\Sub", merged[0]);
    }

    [Fact]
    public void RelocatePathUnderDirectoryRename_matches_app_semantics()
    {
        var np = BrowserTreeSnapshot.RelocatePathUnderDirectoryRename(
            @"C:\Old\a\b.txt",
            @"c:\old",
            @"D:\NewPlace");
        Assert.Equal(@"D:\NewPlace\a\b.txt", np);

        var root = BrowserTreeSnapshot.RelocatePathUnderDirectoryRename(@"C:\Old", @"C:\Old", @"D:\X");
        Assert.Equal(@"D:\X", root);
    }

    [Fact]
    public void RelocateExpandedPaths_relocates_each_entry()
    {
        var list = BrowserTreeSnapshot.RelocateExpandedPaths(
            new[] { @"C:\Old\a", @"C:\Else\q" }.ToList(),
            @"C:\Old",
            @"D:\N");
        Assert.Equal(@"D:\N\a", list[0]);
        Assert.Equal(@"C:\Else\q", list[1]);
    }

    [Fact]
    public void OrderExpandedPathsShallowFirst_orders_by_depth()
    {
        var root = @"C:\r";
        var paths = new[] { @"C:\r\deep\a", @"C:\r\b", @"C:\r\deep" };
        var ordered = BrowserTreeSnapshot.OrderExpandedPathsShallowFirst(paths, root);
        Assert.Equal(@"C:\r\b", ordered[0]);
        Assert.Equal(@"C:\r\deep", ordered[1]);
        Assert.Equal(@"C:\r\deep\a", ordered[2]);
    }

    [Fact]
    public void EnumerateAncestorFolderChain_yields_shallow_to_deep_sequence_for_file()
    {
        var root = @"C:\Share\Album";
        var file = @"C:\Share\Album\2024\03\pic.jpg";
        var chain = BrowserTreeSnapshot.EnumerateAncestorFolderChain(file, root);
        Assert.Equal(new[] { @"C:\Share\Album\2024", @"C:\Share\Album\2024\03" }, chain);
    }
}
