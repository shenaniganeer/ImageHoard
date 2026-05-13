using ImageHoard.Core.Browse;
using ImageHoard.Core.Metrics;
using ImageHoard.Core.Models;

namespace ImageHoard.Tests;

public sealed class FolderDirectorySortTests
{
    [Fact]
    public void SortDirectories_AggregateSize_KnownBeforeUnknown()
    {
        var known = new FileSystemEntry(@"C:\root\known", "known", true, null, DateTimeOffset.UtcNow);
        var unknown = new FileSystemEntry(@"C:\root\unknown", "unknown", true, null, DateTimeOffset.UtcNow);
        var agg = new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase)
        {
            [known.FullPath] = 50L,
        };

        var sorted = FolderDirectorySort.SortDirectories(
            new[] { unknown, known },
            FolderListSortKind.AggregateSize,
            agg);

        Assert.Equal("known", sorted[0].Name);
        Assert.Equal("unknown", sorted[1].Name);
    }

    [Fact]
    public void SortDirectories_AggregateSize_DescendingThenNameTieBreak()
    {
        var small = new FileSystemEntry(@"C:\root\a", "a", true, null, null);
        var big = new FileSystemEntry(@"C:\root\b", "b", true, null, null);
        var agg = new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase)
        {
            [small.FullPath] = 10L,
            [big.FullPath] = 100L,
        };

        var sorted = FolderDirectorySort.SortDirectories(
            new[] { small, big },
            FolderListSortKind.AggregateSize,
            agg);

        Assert.Equal("b", sorted[0].Name);
        Assert.Equal("a", sorted[1].Name);
    }
}

public sealed class FolderMetricsCacheStoreTests
{
    [Fact]
    public async Task TryGetLatestSnapshotForPathAsync_ReturnsLastMatchingRow()
    {
        var path = Path.Combine(Path.GetTempPath(), "ih_fm_cache_" + Guid.NewGuid().ToString("N") + ".jsonl");
        var dir = @"C:\MetricsTest\Sub";
        try
        {
            var v1 = new FolderMetricsSnapshot(dir, 10, 1, 0, DateTimeOffset.UtcNow);
            var v2 = new FolderMetricsSnapshot(dir, 200, 2, 1, DateTimeOffset.UtcNow);
            await FolderMetricsCacheStore.AppendSnapshotAsync(path, v1);
            await FolderMetricsCacheStore.AppendSnapshotAsync(path, v2);

            var latest = await FolderMetricsCacheStore.TryGetLatestSnapshotForPathAsync(path, dir);
            Assert.NotNull(latest);
            Assert.Equal(200, latest.AggregateSizeBytes);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public async Task TryGetLatestSnapshotForPathAsync_ImmediateScope_IgnoresFullSubtreeOnly()
    {
        var path = Path.Combine(Path.GetTempPath(), "ih_fm_cache_scope_" + Guid.NewGuid().ToString("N") + ".jsonl");
        var dir = Path.Combine(Path.GetTempPath(), "ih_fm_only_full_" + Guid.NewGuid().ToString("N"));
        try
        {
            var fullOnly = new FolderMetricsSnapshot(dir, 500, 5, 2, DateTimeOffset.UtcNow, FolderMetricsScanScope.FullSubtree);
            await FolderMetricsCacheStore.AppendSnapshotAsync(path, fullOnly);

            var immediate = await FolderMetricsCacheStore.TryGetLatestSnapshotForPathAsync(
                path,
                dir,
                FolderMetricsScanScope.ImmediateChildren);
            Assert.Null(immediate);

            var full = await FolderMetricsCacheStore.TryGetLatestSnapshotForPathAsync(path, dir, FolderMetricsScanScope.FullSubtree);
            Assert.NotNull(full);
            Assert.Equal(500, full.AggregateSizeBytes);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public async Task TryGetLatestSnapshotForPathAsync_PrefersLatestMatchingScope()
    {
        var path = Path.Combine(Path.GetTempPath(), "ih_fm_cache_scope2_" + Guid.NewGuid().ToString("N") + ".jsonl");
        var dir = Path.Combine(Path.GetTempPath(), "ih_fm_mixed_" + Guid.NewGuid().ToString("N"));
        try
        {
            await FolderMetricsCacheStore.AppendSnapshotAsync(
                path,
                new FolderMetricsSnapshot(dir, 1, 1, 0, null, FolderMetricsScanScope.ImmediateChildren));
            await FolderMetricsCacheStore.AppendSnapshotAsync(
                path,
                new FolderMetricsSnapshot(dir, 900, 9, 3, null, FolderMetricsScanScope.FullSubtree));
            await FolderMetricsCacheStore.AppendSnapshotAsync(
                path,
                new FolderMetricsSnapshot(dir, 2, 2, 1, null, FolderMetricsScanScope.ImmediateChildren));

            var imm = await FolderMetricsCacheStore.TryGetLatestSnapshotForPathAsync(
                path,
                dir,
                FolderMetricsScanScope.ImmediateChildren);
            Assert.NotNull(imm);
            Assert.Equal(2, imm.AggregateSizeBytes);

            var full = await FolderMetricsCacheStore.TryGetLatestSnapshotForPathAsync(path, dir, FolderMetricsScanScope.FullSubtree);
            Assert.NotNull(full);
            Assert.Equal(900, full.AggregateSizeBytes);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // ignored
            }
        }
    }
}
