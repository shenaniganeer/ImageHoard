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

    [Fact]
    public void SortDirectories_ImageFileCount_KnownBeforeUnknown()
    {
        var known = new FileSystemEntry(@"C:\root\known", "known", true, null, DateTimeOffset.UtcNow);
        var unknown = new FileSystemEntry(@"C:\root\unknown", "unknown", true, null, DateTimeOffset.UtcNow);
        var counts = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase)
        {
            [known.FullPath] = 5,
        };
        var emptyAgg = new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase);

        var sorted = FolderDirectorySort.SortDirectories(
            new[] { unknown, known },
            FolderListSortKind.ImageFileCount,
            emptyAgg,
            counts);

        Assert.Equal("known", sorted[0].Name);
        Assert.Equal("unknown", sorted[1].Name);
    }

    [Fact]
    public void SortDirectories_ImageFileCount_DescendingThenNameTieBreak()
    {
        var small = new FileSystemEntry(@"C:\root\a", "a", true, null, null);
        var big = new FileSystemEntry(@"C:\root\b", "b", true, null, null);
        var counts = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase)
        {
            [small.FullPath] = 2,
            [big.FullPath] = 99,
        };
        var emptyAgg = new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase);

        var sorted = FolderDirectorySort.SortDirectories(
            new[] { small, big },
            FolderListSortKind.ImageFileCount,
            emptyAgg,
            counts);

        Assert.Equal("b", sorted[0].Name);
        Assert.Equal("a", sorted[1].Name);
    }

    [Fact]
    public void PickAdjacentSiblingAfterRemoval_ReturnsNextWhenNotLast()
    {
        var a = new FileSystemEntry(@"C:\root\a", "a", true, null, null);
        var b = new FileSystemEntry(@"C:\root\b", "b", true, null, null);
        var c = new FileSystemEntry(@"C:\root\c", "c", true, null, null);
        var sorted = new[] { a, b, c };

        var next = FolderDirectorySort.PickAdjacentSiblingAfterRemoval(sorted, @"C:\root\a");
        Assert.NotNull(next);
        Assert.Equal("b", next.Name);
    }

    [Fact]
    public void PickAdjacentSiblingAfterRemoval_WhenLast_ReturnsPrevious()
    {
        var a = new FileSystemEntry(@"C:\root\a", "a", true, null, null);
        var b = new FileSystemEntry(@"C:\root\b", "b", true, null, null);
        var sorted = new[] { a, b };

        var adj = FolderDirectorySort.PickAdjacentSiblingAfterRemoval(sorted, @"C:\root\b");
        Assert.NotNull(adj);
        Assert.Equal("a", adj.Name);
    }

    [Fact]
    public void PickAdjacentSiblingAfterRemoval_SoleChild_ReturnsNull()
    {
        var only = new FileSystemEntry(@"C:\root\only", "only", true, null, null);
        var adj = FolderDirectorySort.PickAdjacentSiblingAfterRemoval(new[] { only }, @"C:\root\only");
        Assert.Null(adj);
    }

    [Fact]
    public void PickAdjacentSiblingAfterRemoval_UnknownRemoved_ReturnsNull()
    {
        var a = new FileSystemEntry(@"C:\root\a", "a", true, null, null);
        Assert.Null(FolderDirectorySort.PickAdjacentSiblingAfterRemoval(new[] { a }, @"C:\root\missing"));
    }
}

public sealed class ChunkPlannerTests
{
    [Fact]
    public void EnumerateChunks_Empty_YieldsNothing()
    {
        Assert.Empty(ChunkPlanner.EnumerateChunks(0, 10).ToList());
        Assert.Empty(ChunkPlanner.EnumerateChunks(5, 0).ToList());
    }

    [Fact]
    public void EnumerateChunks_SingleChunk_WhenTotalLessThanChunkSize()
    {
        var chunks = ChunkPlanner.EnumerateChunks(3, 10).ToList();
        Assert.Single(chunks);
        Assert.Equal((0, 3), chunks[0]);
    }

    [Fact]
    public void EnumerateChunks_MultipleChunks_LastMayBeShort()
    {
        var chunks = ChunkPlanner.EnumerateChunks(10, 4).ToList();
        Assert.Equal(3, chunks.Count);
        Assert.Equal((0, 4), chunks[0]);
        Assert.Equal((4, 4), chunks[1]);
        Assert.Equal((8, 2), chunks[2]);
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
