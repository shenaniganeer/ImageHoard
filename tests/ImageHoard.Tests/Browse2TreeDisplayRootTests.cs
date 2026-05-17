using ImageHoard.Core.Browse;
using ImageHoard.Core.Browse2;

namespace ImageHoard.Tests;

public sealed class Browse2TreeDisplayRootTests
{
    [Fact]
    public void ClampToWorkspace_outside_index_returns_index_root()
    {
        var diff = new FsDiffStream();
        var ws = new FsMapWorkspace(@"C:\Fav", @"C:\m.json", diff);
        ws.UpsertDirectoryRow(@"C:\Fav", "", "Fav", null, true, 0, 0, 0, null);
        var got = Browse2TreeDisplayRoot.ClampToWorkspace(ws, @"D:\Else");
        Assert.Equal(FavoriteIndexRoots.NormalizeFavoritePath(@"C:\Fav"), got);
    }

    [Fact]
    public void IsSameOrStrictDescendantOf_matches_root_and_children()
    {
        var dr = @"C:\Fav\mid";
        Assert.True(Browse2TreeDisplayRoot.IsSameOrStrictDescendantOf(dr, dr));
        Assert.True(Browse2TreeDisplayRoot.IsSameOrStrictDescendantOf(dr, @"C:\Fav\mid\child"));
        Assert.False(Browse2TreeDisplayRoot.IsSameOrStrictDescendantOf(dr, @"C:\Fav\other"));
    }
}
