using ImageHoard.Core.Browse;
using ImageHoard.Core.Browse2;
using ImageHoard.Core.Services;

namespace ImageHoard.Tests;

public sealed class FsBackgroundScannerTests
{
    [Fact]
    public void StartOnce_second_call_is_ignored()
    {
        var diff = new FsDiffStream();
        var registry = new FsMapRegistry(Path.GetTempPath(), Array.Empty<string>(), diff);
        var scanner = new FsBackgroundScanner();
        using var cts = new CancellationTokenSource();
        scanner.StartOnce(registry, new LocalFileSystem(), cts.Token);
        scanner.StartOnce(registry, new LocalFileSystem(), cts.Token);
        Assert.True(scanner.HasStarted);
        cts.Cancel();
    }

    [Fact]
    public async Task RunAllRootsAsync_YieldEveryDirectory_invokes_directory_visited()
    {
        using var dir = NewTempDir();
        var root = Path.Combine(dir.Path, "Root");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "A"));
        Directory.CreateDirectory(Path.Combine(root, "B"));

        var diff = new FsDiffStream();
        var registry = new FsMapRegistry(dir.Path, new[] { root }, diff);
        await registry.LoadAllAsync();

        var visited = new List<string>();
        var scanner = new FsBackgroundScanner();
        await scanner.RunAllRootsAsync(
            registry,
            new LocalFileSystem(),
            new FsBackgroundScannerOptions
            {
                YieldEveryNDirectories = 1,
                DirectoryVisited = visited.Add,
            },
            CancellationToken.None);

        Assert.Contains(FavoriteIndexRoots.NormalizeFavoritePath(root), visited);
        Assert.Contains(FavoriteIndexRoots.NormalizeFavoritePath(Path.Combine(root, "A")), visited);
    }

    [Fact]
    public async Task RunAllRootsAsync_cancellation_throws()
    {
        using var dir = NewTempDir();
        var root = Path.Combine(dir.Path, "Root");
        Directory.CreateDirectory(root);

        var diff = new FsDiffStream();
        var registry = new FsMapRegistry(dir.Path, new[] { root }, diff);
        await registry.LoadAllAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var scanner = new FsBackgroundScanner();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scanner.RunAllRootsAsync(registry, new LocalFileSystem(), new FsBackgroundScannerOptions(), cts.Token));
    }

    [Fact]
    public async Task RunAllRootsAsync_populates_map_from_disk()
    {
        using var dir = NewTempDir();
        var root = Path.Combine(dir.Path, "Root");
        Directory.CreateDirectory(root);
        var sub = Path.Combine(root, "Sub");
        Directory.CreateDirectory(sub);
        await File.WriteAllTextAsync(Path.Combine(sub, "pic.jpg"), "x");

        var diff = new FsDiffStream();
        var registry = new FsMapRegistry(dir.Path, new[] { root }, diff);
        await registry.LoadAllAsync();

        var scanner = new FsBackgroundScanner();
        await scanner.RunAllRootsAsync(
            registry,
            new LocalFileSystem(),
            new FsBackgroundScannerOptions { YieldEveryNDirectories = 0 },
            CancellationToken.None);

        var ws = registry.TryGetWorkspaceForPath(root)!;
        Assert.True(ws.TryGetEntry(sub, out var e));
        Assert.True(e.TotalFileCount >= 1);
    }

    [Fact]
    public async Task RunAllRootsAsync_skips_transient_workspaces()
    {
        using var dir = NewTempDir();
        var persistentRoot = Path.Combine(dir.Path, "Root");
        Directory.CreateDirectory(persistentRoot);
        var adhocRoot = Path.Combine(dir.Path, "AdHoc");
        Directory.CreateDirectory(adhocRoot);
        Directory.CreateDirectory(Path.Combine(adhocRoot, "Deep"));

        var diff = new FsDiffStream();
        var registry = new FsMapRegistry(dir.Path, new[] { persistentRoot }, diff);
        await registry.LoadAllAsync();
        var transientBrowse = registry.GetOrCreateWorkspaceForBrowseRoot(adhocRoot);
        await transientBrowse.LoadOrCreateEmptyAsync();
        Assert.False(transientBrowse.IsPersistent);

        var visited = new List<string>();
        var scanner = new FsBackgroundScanner();
        await scanner.RunAllRootsAsync(
            registry,
            new LocalFileSystem(),
            new FsBackgroundScannerOptions
            {
                YieldEveryNDirectories = 1,
                DirectoryVisited = visited.Add,
            },
            CancellationToken.None);

        Assert.Contains(FavoriteIndexRoots.NormalizeFavoritePath(persistentRoot), visited);
        Assert.DoesNotContain(FavoriteIndexRoots.NormalizeFavoritePath(adhocRoot), visited);
        Assert.DoesNotContain(FavoriteIndexRoots.NormalizeFavoritePath(Path.Combine(adhocRoot, "Deep")), visited);
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
        var path = Path.Combine(Path.GetTempPath(), "ih-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TempDir(path);
    }
}
