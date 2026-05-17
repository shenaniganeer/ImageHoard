using ImageHoard.Core.Browse;
using ImageHoard.Core.Browse2;

namespace ImageHoard.Tests;

public sealed class FsColdBootPathResolverTests
{
    [Fact]
    public void ResolveSelectedFolder_prefers_deepest_existing_ancestor_of_stored_path()
    {
        var root = @"C:\Archive";
        var diff = new FsDiffStream();
        var ws = new FsMapWorkspace(root, @"C:\Temp\map.json", diff);
        ws.UpsertDirectoryRow(root, "", "Archive", null, true, 0, 0, 0, null);
        var sub = Path.Combine(root, "A");
        ws.UpsertDirectoryRow(sub, root, "A", null, false, 0, 0, 0, null);
        var deep = Path.Combine(sub, "Missing");
        var got = FsColdBootPathResolver.ResolveSelectedFolder(ws, root, deep, null);
        Assert.Equal(FavoriteIndexRoots.NormalizeFavoritePath(sub), got);
    }

    [Fact]
    public void ResolveSelectedFolder_uses_fallback_when_stored_outside_root()
    {
        var root = @"C:\Archive";
        var diff = new FsDiffStream();
        var ws = new FsMapWorkspace(root, @"C:\Temp\map.json", diff);
        ws.UpsertDirectoryRow(root, "", "Archive", null, false, 0, 0, 0, null);
        var got = FsColdBootPathResolver.ResolveSelectedFolder(ws, root, @"D:\Other", root);
        Assert.Equal(FavoriteIndexRoots.NormalizeFavoritePath(root), got);
    }

    [Fact]
    public void ResolveSelectedFolder_empty_map_returns_root()
    {
        var root = @"C:\Archive";
        var diff = new FsDiffStream();
        var ws = new FsMapWorkspace(root, @"C:\Temp\map.json", diff);
        var got = FsColdBootPathResolver.ResolveSelectedFolder(ws, root, null, null);
        Assert.Equal(FavoriteIndexRoots.NormalizeFavoritePath(root), got);
    }

    [Fact]
    public void ResolveSelectedFolderForColdBoot_clamps_persisted_sibling_branch_to_tree_display_root()
    {
        var root = @"C:\Fav";
        var display = Path.Combine(root, "Browse");
        var diff = new FsDiffStream();
        var ws = new FsMapWorkspace(root, @"C:\Temp\map2.json", diff);
        ws.UpsertDirectoryRow(root, "", "Fav", null, true, 0, 0, 0, null);
        ws.UpsertDirectoryRow(display, root, "Browse", null, true, 0, 0, 0, null);
        var child = Path.Combine(display, "c");
        ws.UpsertDirectoryRow(child, display, "c", null, false, 0, 0, 0, null);
        var other = Path.Combine(root, "Other");
        ws.UpsertDirectoryRow(other, root, "Other", null, false, 0, 0, 0, null);

        var got = FsColdBootPathResolver.ResolveSelectedFolderForColdBoot(
            ws,
            root,
            FavoriteIndexRoots.NormalizeFavoritePath(display),
            other,
            FavoriteIndexRoots.NormalizeFavoritePath(display));
        Assert.Equal(FavoriteIndexRoots.NormalizeFavoritePath(display), got);
    }
}
