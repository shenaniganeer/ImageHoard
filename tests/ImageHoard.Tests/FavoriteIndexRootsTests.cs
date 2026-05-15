using ImageHoard.Core.Browse;
using ImageHoard.Core.Metrics;

namespace ImageHoard.Tests;

public sealed class FavoriteIndexRootsTests
{
    [Fact]
    public void ComputeMinimalIndexRoots_DedupesNestedFavorites()
    {
        var roots = FavoriteIndexRoots.ComputeMinimalIndexRoots(new[]
        {
            @"D:\Archive\Projects\Foo",
            @"D:\Archive",
            @"D:\Downloads",
        });

        Assert.Equal(2, roots.Count);
        Assert.Contains(@"D:\Archive", roots, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(@"D:\Downloads", roots, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsStrictSubpath_RespectsDirectoryBoundary()
    {
        Assert.False(FavoriteIndexRoots.IsStrictSubpath(@"C:\ArchiveOld", @"C:\Archive"));
        Assert.True(FavoriteIndexRoots.IsStrictSubpath(@"C:\Archive\Sub", @"C:\Archive"));
    }

    [Fact]
    public void FindOwningIndexRoot_PrefersLongestMatchingRoot()
    {
        var roots = new[] { @"D:\Archive", @"D:\Other" };
        Assert.Equal(@"D:\Archive", FavoriteIndexRoots.FindOwningIndexRoot(@"D:\Archive\Deep\X", roots));
        Assert.Null(FavoriteIndexRoots.FindOwningIndexRoot(@"E:\Nope", roots));
    }
}
