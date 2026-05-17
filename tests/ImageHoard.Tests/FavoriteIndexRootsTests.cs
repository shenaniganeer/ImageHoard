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

    /// <summary>
    /// Image pane selection uses <see cref="FavoriteIndexRoots.NormalizeFavoritePath"/> while directory
    /// enumeration may return logically equivalent strings; matching must use the same normalization.
    /// </summary>
    [Fact]
    public void NormalizeFavoritePath_CollapsesDotDotSoRawListingMatchesCanonicalSelection()
    {
        var temp = Path.Combine(Path.GetTempPath(), "ImageHoardNormTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var leaf = Path.Combine(temp, "a.jpg");
            File.WriteAllText(leaf, "");

            var viaParentDotDot = Path.Combine(temp, "sub", "..", "a.jpg");
            Directory.CreateDirectory(Path.Combine(temp, "sub"));

            var canonical = FavoriteIndexRoots.NormalizeFavoritePath(leaf);
            var rawAlternate = FavoriteIndexRoots.NormalizeFavoritePath(viaParentDotDot);

            Assert.Equal(canonical, rawAlternate, StringComparer.OrdinalIgnoreCase);

            var pathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                FavoriteIndexRoots.NormalizeFavoritePath(leaf),
            };

            Assert.Contains(FavoriteIndexRoots.NormalizeFavoritePath(viaParentDotDot), pathSet);
        }
        finally
        {
            try
            {
                if (Directory.Exists(temp))
                    Directory.Delete(temp, recursive: true);
            }
            catch
            {
                // best-effort cleanup for temp fixture
            }
        }
    }
}
