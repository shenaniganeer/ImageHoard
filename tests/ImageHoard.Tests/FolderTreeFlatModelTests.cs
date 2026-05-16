using ImageHoard.Core.Browse2;

namespace ImageHoard.Tests;

public sealed class FolderTreeFlatModelTests
{
    [Fact]
    public async Task Rebuild_implicit_root_shows_immediate_children_in_natural_order_without_expansion_state()
    {
        using var dir = NewTempDir();
        var root = Path.Combine(dir.Path, "Root");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "10"));
        Directory.CreateDirectory(Path.Combine(root, "2"));

        var diff = new FsDiffStream();
        var registry = new FsMapRegistry(dir.Path, new[] { root }, diff);
        await registry.LoadAllAsync();
        var ws = registry.TryGetWorkspaceForPath(root)!;
        ws.UpsertDirectoryRow(root, "", "Root", DateTimeOffset.UtcNow, true, 0, 0, 0, DateTimeOffset.UtcNow);
        ws.UpsertDirectoryRow(
            Path.Combine(root, "10"),
            root,
            "10",
            DateTimeOffset.UtcNow,
            false,
            0,
            0,
            0,
            DateTimeOffset.UtcNow);
        ws.UpsertDirectoryRow(
            Path.Combine(root, "2"),
            root,
            "2",
            DateTimeOffset.UtcNow,
            false,
            0,
            0,
            0,
            DateTimeOffset.UtcNow);

        using var model = new FolderTreeFlatModel(ws, diff, subscribeToDiffStream: false);
        model.Rebuild();
        Assert.Equal(2, model.Rows.Count);
        Assert.Equal(0, model.Rows[0].Depth);
        Assert.Equal(0, model.Rows[1].Depth);
        Assert.Equal(Path.Combine(root, "2"), model.Rows[0].Path);
        Assert.Equal(Path.Combine(root, "10"), model.Rows[1].Path);
        Assert.Equal(0, model.RowIndexByPath[Path.Combine(root, "2")]);
        Assert.Equal(1, model.RowIndexByPath[Path.Combine(root, "10")]);
    }

    [Fact]
    public async Task Collapse_index_root_is_noop()
    {
        using var dir = NewTempDir();
        var root = Path.Combine(dir.Path, "Root");
        Directory.CreateDirectory(root);

        var diff = new FsDiffStream();
        var registry = new FsMapRegistry(dir.Path, new[] { root }, diff);
        await registry.LoadAllAsync();
        var ws = registry.TryGetWorkspaceForPath(root)!;
        ws.UpsertDirectoryRow(root, "", "Root", DateTimeOffset.UtcNow, false, 0, 0, 0, DateTimeOffset.UtcNow);

        using var model = new FolderTreeFlatModel(ws, diff, subscribeToDiffStream: false);
        model.Rebuild();
        Assert.Empty(model.Rows);
        Assert.True(model.Collapse(root).IsEmpty);
    }

    [Fact]
    public async Task Expand_Collapse_SplicesDescendantRange()
    {
        using var dir = NewTempDir();
        var root = Path.Combine(dir.Path, "Root");
        var child = Path.Combine(root, "c");
        Directory.CreateDirectory(child);

        var diff = new FsDiffStream();
        var registry = new FsMapRegistry(dir.Path, new[] { root }, diff);
        await registry.LoadAllAsync();
        var ws = registry.TryGetWorkspaceForPath(root)!;
        ws.UpsertDirectoryRow(root, "", "Root", DateTimeOffset.UtcNow, true, 0, 0, 0, DateTimeOffset.UtcNow);
        ws.UpsertDirectoryRow(child, root, "c", DateTimeOffset.UtcNow, false, 0, 0, 0, DateTimeOffset.UtcNow);

        using var model = new FolderTreeFlatModel(ws, diff, subscribeToDiffStream: false);
        model.Rebuild();
        Assert.Single(model.Rows);
        Assert.Equal(child, model.Rows[0].Path);
        Assert.Equal(0, model.Rows[0].Depth);

        var dExpand = model.Expand(child);
        Assert.False(dExpand.IsEmpty);
        Assert.Single(model.Rows);

        var dCollapse = model.Collapse(child);
        Assert.False(dCollapse.IsEmpty);
        Assert.Single(model.Rows);
        Assert.False(model.Rows[0].IsExpanded);
    }

    [Fact]
    public async Task ApplyDiff_FolderAdded_InsertsUnderExpandedParent()
    {
        using var dir = NewTempDir();
        var root = Path.Combine(dir.Path, "Root");
        Directory.CreateDirectory(root);

        var diff = new FsDiffStream();
        var registry = new FsMapRegistry(dir.Path, new[] { root }, diff);
        await registry.LoadAllAsync();
        var ws = registry.TryGetWorkspaceForPath(root)!;
        ws.UpsertDirectoryRow(root, "", "Root", DateTimeOffset.UtcNow, true, 0, 0, 0, DateTimeOffset.UtcNow);

        using var model = new FolderTreeFlatModel(ws, diff, subscribeToDiffStream: false);
        model.Rebuild();
        Assert.Empty(model.Rows);

        var newChild = Path.Combine(root, "New");
        ws.UpsertDirectoryRow(
            newChild,
            root,
            "New",
            DateTimeOffset.UtcNow,
            false,
            0,
            0,
            0,
            DateTimeOffset.UtcNow);

        var snap = new FsMapEntry { Name = "New", ParentPath = root, HasSubfolders = false };
        var d = model.ApplyDiff(new FsFolderAddedDiff(ws.IndexRoot, newChild, root, snap));
        Assert.False(d.IsEmpty);
        Assert.Single(model.Rows);
        Assert.Equal(newChild, model.Rows[0].Path);
    }

    [Fact]
    public async Task ApplyDiff_FolderRemoved_RemovesContiguousSubtreeBlock()
    {
        using var dir = NewTempDir();
        var root = Path.Combine(dir.Path, "Root");
        var a = Path.Combine(root, "a");
        var nested = Path.Combine(a, "n");
        Directory.CreateDirectory(nested);

        var diff = new FsDiffStream();
        var registry = new FsMapRegistry(dir.Path, new[] { root }, diff);
        await registry.LoadAllAsync();
        var ws = registry.TryGetWorkspaceForPath(root)!;
        ws.UpsertDirectoryRow(root, "", "Root", DateTimeOffset.UtcNow, true, 0, 0, 0, DateTimeOffset.UtcNow);
        ws.UpsertDirectoryRow(a, root, "a", DateTimeOffset.UtcNow, true, 0, 0, 0, DateTimeOffset.UtcNow);
        ws.UpsertDirectoryRow(nested, a, "n", DateTimeOffset.UtcNow, false, 0, 0, 0, DateTimeOffset.UtcNow);

        using var model = new FolderTreeFlatModel(ws, diff, subscribeToDiffStream: false);
        model.Expansion.TryExpand(a);
        model.Rebuild();
        Assert.Equal(2, model.Rows.Count);

        ws.RemoveSubtree(a);
        var d = model.ApplyDiff(new FsFolderRemovedDiff(ws.IndexRoot, a, root));
        Assert.False(d.IsEmpty);
        Assert.Empty(model.Rows);
    }

    [Fact]
    public async Task ApplyDiff_FolderRenamed_ReinsertsWithAdjustedIndex()
    {
        using var dir = NewTempDir();
        var root = Path.Combine(dir.Path, "Root");
        var oldP = Path.Combine(root, "old");
        Directory.CreateDirectory(oldP);

        var diff = new FsDiffStream();
        var registry = new FsMapRegistry(dir.Path, new[] { root }, diff);
        await registry.LoadAllAsync();
        var ws = registry.TryGetWorkspaceForPath(root)!;
        ws.UpsertDirectoryRow(root, "", "Root", DateTimeOffset.UtcNow, true, 0, 0, 0, DateTimeOffset.UtcNow);
        ws.UpsertDirectoryRow(oldP, root, "old", DateTimeOffset.UtcNow, false, 0, 0, 0, DateTimeOffset.UtcNow);

        using var model = new FolderTreeFlatModel(ws, diff, subscribeToDiffStream: false);
        model.Rebuild();
        Assert.Single(model.Rows);
        Assert.Equal(oldP, model.Rows[0].Path);

        var newP = Path.Combine(root, "zebra");
        ws.RemapSubtreePrefix(oldP, newP);

        var d = model.ApplyDiff(
            new FsFolderRenamedDiff(ws.IndexRoot, oldP, newP, root, root));
        Assert.False(d.IsEmpty);
        Assert.Single(model.Rows);
        Assert.Equal(newP, model.Rows[0].Path);
        Assert.Equal("zebra", model.Rows[0].Name);
    }

    [Fact]
    public async Task DiffStream_Subscription_EmitsDelta()
    {
        using var dir = NewTempDir();
        var root = Path.Combine(dir.Path, "Root");
        Directory.CreateDirectory(root);

        var diff = new FsDiffStream();
        var registry = new FsMapRegistry(dir.Path, new[] { root }, diff);
        await registry.LoadAllAsync();
        var ws = registry.TryGetWorkspaceForPath(root)!;
        ws.UpsertDirectoryRow(root, "", "Root", DateTimeOffset.UtcNow, true, 0, 0, 0, DateTimeOffset.UtcNow);

        FlatModelDelta? seen = null;
        using var model = new FolderTreeFlatModel(ws, diff, subscribeToDiffStream: true);
        model.Rebuild();
        model.DeltaReceived += d => seen = d;

        var child = Path.Combine(root, "c");
        ws.UpsertDirectoryRow(child, root, "c", DateTimeOffset.UtcNow, false, 0, 0, 0, DateTimeOffset.UtcNow);

        Assert.NotNull(seen);
        Assert.False(seen!.IsEmpty);
    }

    [Fact]
    public async Task Rebuild_twice_row_index_by_path_stays_consistent()
    {
        using var dir = NewTempDir();
        var root = Path.Combine(dir.Path, "Root");
        Directory.CreateDirectory(root);

        var diff = new FsDiffStream();
        var registry = new FsMapRegistry(dir.Path, new[] { root }, diff);
        await registry.LoadAllAsync();
        var ws = registry.TryGetWorkspaceForPath(root)!;
        ws.UpsertDirectoryRow(root, "", "Root", DateTimeOffset.UtcNow, false, 0, 0, 0, DateTimeOffset.UtcNow);

        using var model = new FolderTreeFlatModel(ws, diff, subscribeToDiffStream: false);
        model.Rebuild();
        model.Rebuild();
        Assert.Empty(model.RowIndexByPath);
    }

    [Fact]
    public async Task Expand_already_expanded_returns_empty_delta()
    {
        using var dir = NewTempDir();
        var root = Path.Combine(dir.Path, "Root");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "c"));

        var diff = new FsDiffStream();
        var registry = new FsMapRegistry(dir.Path, new[] { root }, diff);
        await registry.LoadAllAsync();
        var ws = registry.TryGetWorkspaceForPath(root)!;
        ws.UpsertDirectoryRow(root, "", "Root", DateTimeOffset.UtcNow, true, 0, 0, 0, DateTimeOffset.UtcNow);
        ws.UpsertDirectoryRow(
            Path.Combine(root, "c"),
            root,
            "c",
            DateTimeOffset.UtcNow,
            false,
            0,
            0,
            0,
            DateTimeOffset.UtcNow);

        using var model = new FolderTreeFlatModel(ws, diff, subscribeToDiffStream: false);
        model.Rebuild();
        var again = model.Expand(root);
        Assert.True(again.IsEmpty);
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
        var path = Path.Combine(Path.GetTempPath(), "ih-flat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TempDir(path);
    }
}
