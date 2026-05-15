using ImageHoard.Core.Services;
using ImageHoard.Core.Slideshow;

namespace ImageHoard.Tests;

public sealed class SlideshowCoordinatorTests
{
    [Fact]
    public async Task Sibling_overlay_does_not_advance_tree_until_tree_nav()
    {
        var root = Path.Combine(Path.GetTempPath(), "ih_sib_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "a.jpg"), "x");
        await File.WriteAllTextAsync(Path.Combine(root, "b.jpg"), "y");

        try
        {
            var fs = new LocalFileSystem();
            var tree = new TreeSlideshowSession(fs, new Random(0));
            var coord = new SlideshowCoordinator(tree);
            coord.Tree.Start(root);
            await coord.Tree.WaitForInitialPoolAsync();
            Assert.True(coord.Tree.TryMoveNext(out var first));
            Assert.NotNull(first);

            Assert.False(coord.IsSiblingOverlayActive);
            Assert.True(await coord.TryMoveNextSiblingAsync(fs, first));
            Assert.True(coord.IsSiblingOverlayActive);
            var sib = coord.GetCurrentPath();
            Assert.NotNull(sib);
            Assert.NotEqual(first, sib);

            Assert.True(coord.TryMoveNextTree(out var treeNext));
            Assert.False(coord.IsSiblingOverlayActive);
            Assert.NotNull(treeNext);

            coord.Tree.StopEnumeration();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TryGetSlideshowOverlayListPosition_sibling_matches_folder_index()
    {
        var root = Path.Combine(Path.GetTempPath(), "ih_sibov_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "a.jpg"), "x");
        await File.WriteAllTextAsync(Path.Combine(root, "b.jpg"), "y");

        try
        {
            var fs = new LocalFileSystem();
            var tree = new TreeSlideshowSession(fs, new Random(0));
            var coord = new SlideshowCoordinator(tree);
            coord.Tree.Start(root);
            await coord.Tree.WaitForInitialPoolAsync();
            Assert.True(coord.Tree.TryMoveNext(out var first));
            Assert.NotNull(first);
            var sortedIndex =
                string.Equals(Path.GetFileName(first!), "a.jpg", StringComparison.OrdinalIgnoreCase) ? 1 : 2;

            Assert.True(coord.TryGetSlideshowOverlayListPosition(out var treeIdx, out var treeTotal, out _));
            Assert.Equal(1, treeIdx);
            Assert.True(treeTotal >= 2);

            Assert.True(await coord.TryMoveNextSiblingAsync(fs, first));
            Assert.True(coord.TryGetSlideshowOverlayListPosition(out var folderIdx, out var folderTotal, out var folderDone));
            Assert.Equal(sortedIndex == 1 ? 2 : 1, folderIdx);
            Assert.Equal(2, folderTotal);
            Assert.True(folderDone);

            coord.Tree.StopEnumeration();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
