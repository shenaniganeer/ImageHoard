using ImageHoard.Core.Browse2;
using ImageHoard.Core.Services;

namespace ImageHoard.Tests;

public sealed class FsTargetedRefresherAggregatesTests
{
    [Fact]
    public async Task RefreshAggregatesForDirectChildrenAsync_updates_existing_child_rows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ih-browse2-agg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var root = Path.Combine(dir, "Root");
            Directory.CreateDirectory(root);
            var child = Path.Combine(root, "Sub");
            Directory.CreateDirectory(child);
            var img = Path.Combine(child, "a.jpg");
            await File.WriteAllBytesAsync(img, new byte[200]);

            var diff = new FsDiffStream();
            var registry = new FsMapRegistry(dir, new[] { root }, diff);
            await registry.LoadAllAsync();
            var ws = registry.TryGetWorkspaceForPath(root)!;
            ws.UpsertDirectoryRow(
                root,
                "",
                "Root",
                DateTimeOffset.UtcNow,
                true,
                0,
                0,
                0,
                DateTimeOffset.UtcNow);
            ws.UpsertDirectoryRow(
                child,
                root,
                "Sub",
                DateTimeOffset.UtcNow,
                false,
                0,
                0,
                0,
                lastVerifiedAtUtc: null);

            var refresher = new FsTargetedRefresher(new LocalFileSystem(), registry);
            await refresher.RefreshAggregatesForDirectChildrenAsync(root);

            Assert.True(ws.TryGetEntry(child, out var e));
            Assert.NotNull(e.LastVerifiedAtUtc);
            Assert.True(e.AggregateSizeBytes >= 200);
            Assert.True(e.ImageFileCount >= 1);
            Assert.True(e.TotalFileCount >= 1);
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
