using ImageHoard.Core.Browse2;
using ImageHoard.Core.Services;

namespace ImageHoard.Tests;

public sealed class FsChangeApplierTests
{
    [Fact]
    public async Task ApplyRecycleAsync_removes_subtree_from_workspace()
    {
        using var dir = NewTempDir();
        var root = Path.Combine(dir.Path, "Root");
        var victim = Path.Combine(root, "gone");
        Directory.CreateDirectory(victim);
        Directory.CreateDirectory(Path.Combine(victim, "n"));

        var diff = new FsDiffStream();
        var registry = new FsMapRegistry(dir.Path, new[] { root }, diff);
        await registry.LoadAllAsync();
        var ws = registry.TryGetWorkspaceForPath(root)!;
        ws.UpsertDirectoryRow(root, "", "Root", DateTimeOffset.UtcNow, true, 0, 0, 0, DateTimeOffset.UtcNow);
        ws.UpsertDirectoryRow(
            victim,
            root,
            "gone",
            DateTimeOffset.UtcNow,
            true,
            0,
            0,
            0,
            DateTimeOffset.UtcNow);
        var nested = Path.Combine(victim, "n");
        ws.UpsertDirectoryRow(
            nested,
            victim,
            "n",
            DateTimeOffset.UtcNow,
            false,
            0,
            0,
            0,
            DateTimeOffset.UtcNow);

        var applier = new FsChangeApplier(new LocalFileSystem(), registry);
        await applier.ApplyRecycleAsync(registry, victim);

        Assert.False(ws.TryGetEntry(victim, out _));
        Assert.False(ws.TryGetEntry(nested, out _));
        Assert.True(ws.TryGetEntry(root, out _));
    }

    [Fact]
    public async Task ApplyWizardRemovedImageFilesAsync_updates_multiple_workspaces()
    {
        using var dir = NewTempDir();
        var root1 = Path.Combine(dir.Path, "R1");
        var root2 = Path.Combine(dir.Path, "R2");
        Directory.CreateDirectory(root1);
        Directory.CreateDirectory(root2);
        var f1 = Path.Combine(root1, "a.jpg");
        var f2 = Path.Combine(root2, "b.jpg");
        await File.WriteAllTextAsync(f1, "x");
        await File.WriteAllTextAsync(f2, "y");

        var diff = new FsDiffStream();
        var registry = new FsMapRegistry(dir.Path, new[] { root1, root2 }, diff);
        await registry.LoadAllAsync();
        var ws1 = registry.TryGetWorkspaceForPath(root1)!;
        var ws2 = registry.TryGetWorkspaceForPath(root2)!;
        ws1.UpsertDirectoryRow(root1, "", "R1", null, false, 100, 3, 2, null);
        ws2.UpsertDirectoryRow(root2, "", "R2", null, false, 50, 2, 1, null);

        var applier = new FsChangeApplier(new LocalFileSystem(), registry);
        await applier.ApplyWizardRemovedImageFilesAsync(
            registry,
            new List<(string FullPath, long LengthBytes, bool IsImage)>
            {
                (f1, 10, true),
                (f2, 5, true),
            });

        Assert.True(ws1.TryGetEntry(root1, out var e1));
        Assert.Equal(90, e1.AggregateSizeBytes);
        Assert.True(ws2.TryGetEntry(root2, out var e2));
        Assert.Equal(45, e2.AggregateSizeBytes);
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir(string path) => Path = path;

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // ignored
            }
        }
    }

    private static TempDir NewTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "ih-cap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TempDir(path);
    }
}
