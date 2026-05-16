using ImageHoard.Core.Browse;
using ImageHoard.Core.Browse2;
using ImageHoard.Core.Services;

namespace ImageHoard.Tests;

public sealed class Browse2FsMapTests
{
    [Fact]
    public async Task Registry_LoadSave_RoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ih-browse2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var root = Path.Combine(dir, "Root");
            Directory.CreateDirectory(root);
            var child = Path.Combine(root, "Child");
            Directory.CreateDirectory(child);

            var diff = new FsDiffStream();
            var registry = new FsMapRegistry(dir, new[] { root }, diff);
            await registry.LoadAllAsync();

            var ws = registry.TryGetWorkspaceForPath(root);
            Assert.NotNull(ws);
            ws!.UpsertDirectoryRow(
                root,
                "",
                "Root",
                DateTimeOffset.UtcNow,
                true,
                1,
                1,
                0,
                DateTimeOffset.UtcNow);
            await ws.SaveAsync();

            var registry2 = new FsMapRegistry(dir, new[] { root }, new FsDiffStream());
            await registry2.LoadAllAsync();
            Assert.True(registry2.TryGetWorkspaceForPath(root)!.TryGetEntry(root, out var e));
            Assert.Equal(1, e.TotalFileCount);
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

    [Fact]
    public async Task Registry_LoadAll_seeds_placeholder_index_root_when_map_empty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ih-browse2-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var root = Path.Combine(dir, "Root");
            Directory.CreateDirectory(root);

            var registry = new FsMapRegistry(dir, new[] { root }, new FsDiffStream());
            await registry.LoadAllAsync();
            var ws = registry.TryGetWorkspaceForPath(root)!;
            Assert.True(ws.TryGetEntry(root, out var e));
            Assert.True(e.HasSubfolders);
            Assert.Null(e.LastVerifiedAtUtc);
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

    [Fact]
    public async Task TargetedRefresher_AddsChildRows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ih-browse2-tr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var root = Path.Combine(dir, "Root");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "A"));
            Directory.CreateDirectory(Path.Combine(root, "B"));

            var diff = new FsDiffStream();
            var registry = new FsMapRegistry(dir, new[] { root }, diff);
            await registry.LoadAllAsync();
            var fs = new LocalFileSystem();
            var refresher = new FsTargetedRefresher(fs, registry);

            await refresher.RefreshAsync(root);

            var ws = registry.TryGetWorkspaceForPath(root)!;
            Assert.True(ws.TryGetEntry(root, out _));
            Assert.True(ws.TryGetEntry(Path.Combine(root, "A"), out var a));
            Assert.True(ws.TryGetEntry(Path.Combine(root, "B"), out var b));
            Assert.Equal("A", a.Name);
            Assert.Equal("B", b.Name);
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

    [Fact]
    public async Task ChangeApplier_RemapWithinRoot_UpdatesKeys()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ih-browse2-mv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var root = Path.Combine(dir, "Root");
            var oldPath = Path.Combine(root, "Old");
            var newPath = Path.Combine(root, "New");
            Directory.CreateDirectory(oldPath);
            Directory.CreateDirectory(Path.Combine(oldPath, "Nested"));

            var diff = new FsDiffStream();
            var registry = new FsMapRegistry(dir, new[] { root }, diff);
            await registry.LoadAllAsync();
            var ws = registry.TryGetWorkspaceForPath(root)!;
            ws.UpsertDirectoryRow(root, "", "Root", DateTimeOffset.UtcNow, true, 0, 0, 0, DateTimeOffset.UtcNow);
            ws.UpsertDirectoryRow(
                oldPath,
                root,
                "Old",
                DateTimeOffset.UtcNow,
                true,
                0,
                0,
                0,
                DateTimeOffset.UtcNow);
            var nested = Path.Combine(oldPath, "Nested");
            ws.UpsertDirectoryRow(
                nested,
                oldPath,
                "Nested",
                DateTimeOffset.UtcNow,
                false,
                0,
                0,
                0,
                DateTimeOffset.UtcNow);
            await ws.SaveAsync();

            Directory.CreateDirectory(newPath);
            Directory.Move(nested, Path.Combine(newPath, "Nested"));

            var applier = new FsChangeApplier(new LocalFileSystem(), registry);
            await applier.ApplyDirectoryMoveAsync(registry, oldPath, newPath);

            Assert.False(ws.TryGetEntry(oldPath, out _));
            Assert.True(ws.TryGetEntry(newPath, out var newRow));
            Assert.Equal("New", newRow.Name);
            var nestedNew = Path.Combine(newPath, "Nested");
            Assert.True(ws.TryGetEntry(nestedNew, out var nn));
            Assert.Equal("Nested", nn.Name);
            Assert.Equal(newPath, nn.ParentPath, StringComparer.OrdinalIgnoreCase);
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

    [Fact]
    public void Workspace_ApplyWizardRemovedImageFileStats_DecrementsAncestorAggregates()
    {
        var diff = new FsDiffStream();
        var root = Path.Combine(Path.GetTempPath(), "ih-browse2-wiz-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ws = new FsMapWorkspace(root, Path.Combine(root, "map.json"), diff);
            ws.UpsertDirectoryRow(root, "", "root", null, false, 100, 5, 2, DateTimeOffset.UtcNow);
            var sub = Path.Combine(root, "Sub");
            ws.UpsertDirectoryRow(sub, root, "Sub", null, false, 50, 3, 1, DateTimeOffset.UtcNow);
            var file = Path.Combine(sub, "a.jpg");
            ws.ApplyWizardRemovedImageFileStats(
                new List<(string FullPath, long LengthBytes, bool IsImage)>
                {
                    (file, 10, true),
                });

            Assert.True(ws.TryGetEntry(sub, out var se));
            Assert.Equal(40, se.AggregateSizeBytes);
            Assert.Equal(2, se.TotalFileCount);
            Assert.Equal(0, se.ImageFileCount);
            Assert.True(ws.TryGetEntry(root, out var re));
            Assert.Equal(90, re.AggregateSizeBytes);
            Assert.Equal(4, re.TotalFileCount);
            Assert.Equal(1, re.ImageFileCount);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public void Registry_GetOrCreateWorkspaceForBrowseRoot_is_idempotent()
    {
        var diff = new FsDiffStream();
        var registry = new FsMapRegistry(Path.GetTempPath(), Array.Empty<string>(), diff);
        var path = FavoriteIndexRoots.NormalizeFavoritePath(Path.Combine(Path.GetTempPath(), "transient-" + Guid.NewGuid().ToString("N")));
        var a = registry.GetOrCreateWorkspaceForBrowseRoot(path);
        var b = registry.GetOrCreateWorkspaceForBrowseRoot(path);
        Assert.Same(a, b);
        Assert.False(a.IsPersistent);
    }

    [Fact]
    public void Registry_HasPersistentWorkspaceFor_true_only_for_favorite_index_roots()
    {
        var diff = new FsDiffStream();
        var dir = Path.Combine(Path.GetTempPath(), "ih-b2-hasp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var root = Path.Combine(dir, "Root");
            Directory.CreateDirectory(root);
            var registry = new FsMapRegistry(dir, new[] { root }, diff);
            Assert.True(registry.HasPersistentWorkspaceFor(root));
            Assert.False(registry.HasPersistentWorkspaceFor(Path.Combine(root, "child")));
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

    [Fact]
    public async Task Registry_TryGetWorkspaceForPath_resolves_under_transient_browse_root()
    {
        var diff = new FsDiffStream();
        var registry = new FsMapRegistry(Path.GetTempPath(), Array.Empty<string>(), diff);
        await registry.LoadAllAsync();
        var root = FavoriteIndexRoots.NormalizeFavoritePath(Path.Combine(Path.GetTempPath(), "transient2-" + Guid.NewGuid().ToString("N")));
        var child = Path.Combine(root, "child");
        var ws = registry.GetOrCreateWorkspaceForBrowseRoot(root);
        await ws.LoadOrCreateEmptyAsync();
        var resolved = registry.TryGetWorkspaceForPath(child);
        Assert.Same(ws, resolved);
    }

    [Fact]
    public async Task FsMapRegistry_TryGetWorkspaceForPath_prefers_persistent_favorite_over_transient_child_root()
    {
        var diff = new FsDiffStream();
        var baseDir = Path.Combine(Path.GetTempPath(), "ih-b2-promote-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);
        var photos = Path.Combine(baseDir, "photos");
        var year = Path.Combine(photos, "2024");
        Directory.CreateDirectory(year);

        try
        {
            var maps1 = Path.Combine(baseDir, "maps1");
            Directory.CreateDirectory(maps1);
            var r1 = new FsMapRegistry(maps1, Array.Empty<string>(), diff);
            await r1.LoadAllAsync();
            var transient = r1.GetOrCreateWorkspaceForBrowseRoot(FavoriteIndexRoots.NormalizeFavoritePath(year));
            await transient.LoadOrCreateEmptyAsync();
            Assert.False(transient.IsPersistent);
            Assert.Same(transient, r1.TryGetWorkspaceForPath(year));

            var diff2 = new FsDiffStream();
            var maps2 = Path.Combine(baseDir, "maps2");
            Directory.CreateDirectory(maps2);
            var r2 = new FsMapRegistry(maps2, new[] { photos }, diff2);
            await r2.LoadAllAsync();
            var persistent = r2.TryGetWorkspaceForPath(year);
            Assert.NotNull(persistent);
            Assert.True(persistent!.IsPersistent);
            Assert.Equal(FavoriteIndexRoots.NormalizeFavoritePath(photos), persistent.IndexRoot);
            Assert.NotSame(transient, persistent);
        }
        finally
        {
            try
            {
                Directory.Delete(baseDir, recursive: true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public void BackgroundScanner_StartOnce_IsIdempotent()
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
}
