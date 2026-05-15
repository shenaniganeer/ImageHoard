using ImageHoard.Core.Browse;
using ImageHoard.Core.Metrics;

namespace ImageHoard.Tests;

public sealed class FavoriteFilesystemMapStoreTests
{
    [Fact]
    public async Task UpsertAndLoad_RoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ih-fsmap-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var root = @"C:\Data\Root";
            var snap = new FolderMetricsSnapshot(
                @"C:\Data\Root\Child",
                1024,
                3,
                1,
                DateTimeOffset.UtcNow,
                FolderMetricsScanScope.FullSubtree,
                null);
            await FavoriteFilesystemMapStore.TryUpsertSubtreeSnapshotAsync(dir, root, snap);
            var path = FavoriteFilesystemMapStore.MapFilePathForIndexRoot(dir, root);
            var doc = await FavoriteFilesystemMapStore.TryLoadAsync(path);
            Assert.NotNull(doc);
            Assert.True(doc!.Entries.TryGetValue(@"C:\Data\Root\Child", out var e));
            Assert.Equal(1024, e.AggregateSizeBytes);
            Assert.Equal(1, e.ImageFileCount);

            await FavoriteFilesystemMapStore
                .PurgePathsAsync(dir, FavoriteIndexRoots.ComputeMinimalIndexRoots(new[] { root }), new[] { @"C:\Data\Root\Child" });
            var doc2 = await FavoriteFilesystemMapStore.TryLoadAsync(path);
            Assert.NotNull(doc2);
            Assert.False(doc2!.Entries.ContainsKey(@"C:\Data\Root\Child"));
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // ignored
            }
        }
    }
}
