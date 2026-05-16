using ImageHoard.Core.Browse;
using ImageHoard.Core.Browse2;

namespace ImageHoard.Tests;

public sealed class FsMapWorkspaceTests
{
    [Fact]
    public async Task LoadOrCreateEmptyAsync_in_memory_skips_disk_and_seeds_root()
    {
        var diff = new FsDiffStream();
        var root = FavoriteIndexRoots.NormalizeFavoritePath(Path.Combine(Path.GetTempPath(), "ih-ws-mem-" + Guid.NewGuid().ToString("N")));
        var ws = new FsMapWorkspace(root, mapFilePath: "", diff);
        Assert.False(ws.IsPersistent);
        await ws.LoadOrCreateEmptyAsync();
        Assert.True(ws.TryGetEntry(root, out var e));
        Assert.True(e.HasSubfolders);
        Assert.Null(e.LastVerifiedAtUtc);
    }

    [Fact]
    public async Task SaveAsync_in_memory_does_not_throw()
    {
        var diff = new FsDiffStream();
        var root = FavoriteIndexRoots.NormalizeFavoritePath(Path.Combine(Path.GetTempPath(), "ih-ws-save-" + Guid.NewGuid().ToString("N")));
        var ws = new FsMapWorkspace(root, mapFilePath: "", diff);
        await ws.LoadOrCreateEmptyAsync();
        await ws.SaveAsync();
    }

    [Fact]
    public void IsPersistent_true_when_map_file_path_non_empty()
    {
        var diff = new FsDiffStream();
        var ws = new FsMapWorkspace("C:\\root", "C:\\cache\\map.json", diff);
        Assert.True(ws.IsPersistent);
    }
}
