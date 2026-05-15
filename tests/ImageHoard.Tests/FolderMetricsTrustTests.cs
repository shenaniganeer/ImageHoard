using ImageHoard.Core.Metrics;

namespace ImageHoard.Tests;

public sealed class FolderMetricsTrustTests
{
    [Fact]
    public void FolderMtimeMatches_both_null()
    {
        Assert.True(FolderMetricsTrust.FolderMtimeMatches(null, null));
    }

    [Fact]
    public void FolderMtimeMatches_one_null()
    {
        var t = DateTimeOffset.UtcNow;
        Assert.False(FolderMetricsTrust.FolderMtimeMatches(t, null));
        Assert.False(FolderMetricsTrust.FolderMtimeMatches(null, t));
    }

    [Fact]
    public void FolderMtimeMatches_equal_values()
    {
        var t = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.True(FolderMetricsTrust.FolderMtimeMatches(t, t));
        Assert.True(FolderMetricsTrust.FolderMtimeMatches(t, new DateTimeOffset(t.UtcTicks, TimeSpan.Zero)));
    }

    [Fact]
    public void FolderMtimeMatches_different_values()
    {
        var a = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var b = new DateTimeOffset(2024, 6, 2, 12, 0, 0, TimeSpan.Zero);
        Assert.False(FolderMetricsTrust.FolderMtimeMatches(a, b));
    }

    [Fact]
    public void IsTrustedCachedSubtree_false_when_ignore_cache()
    {
        var snap = new FolderMetricsSnapshot(
            "C:\\x",
            10,
            1,
            1,
            DateTimeOffset.UtcNow,
            FolderMetricsScanScope.FullSubtree);
        Assert.False(FolderMetricsTrust.IsTrustedCachedSubtree(snap, snap.FolderMtimeUtc, ignoreCache: true));
    }

    [Fact]
    public void IsTrustedCachedSubtree_false_when_snapshot_null()
    {
        Assert.False(FolderMetricsTrust.IsTrustedCachedSubtree(null, DateTimeOffset.UtcNow, ignoreCache: false));
    }

    [Fact]
    public void IsTrustedCachedSubtree_false_when_scope_is_immediate()
    {
        var m = DateTimeOffset.UtcNow;
        var snap = new FolderMetricsSnapshot("C:\\x", 1, 1, 1, m, FolderMetricsScanScope.ImmediateChildren, true);
        Assert.False(FolderMetricsTrust.IsTrustedCachedSubtree(snap, m, ignoreCache: false));
    }

    [Fact]
    public void IsTrustedCachedSubtree_false_when_mtime_mismatch()
    {
        var snap = new FolderMetricsSnapshot(
            "C:\\x",
            10,
            2,
            1,
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            FolderMetricsScanScope.FullSubtree);
        var onDisk = new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero);
        Assert.False(FolderMetricsTrust.IsTrustedCachedSubtree(snap, onDisk, ignoreCache: false));
    }

    [Fact]
    public void IsTrustedCachedSubtree_true_for_full_subtree_matching_mtime()
    {
        var m = new DateTimeOffset(2024, 3, 15, 8, 30, 0, TimeSpan.Zero);
        var snap = new FolderMetricsSnapshot("C:\\photos", 999, 5, 3, m, FolderMetricsScanScope.FullSubtree);
        Assert.True(FolderMetricsTrust.IsTrustedCachedSubtree(snap, m, ignoreCache: false));
    }
}
