using ImageHoard.Core.Services;
using ImageHoard.Core.Slideshow;

namespace ImageHoard.Tests;

public sealed class SlideshowCoordinatorTests
{
    [Fact]
    public async Task ToggleScope_switches_between_tree_and_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "ih_scope_" + Guid.NewGuid().ToString("N"));
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

            await coord.ToggleScopeAsync(fs, first);
            Assert.Equal(SlideshowScopeKind.Folder, coord.Scope);

            await coord.ToggleScopeAsync(fs, first);
            Assert.Equal(SlideshowScopeKind.Tree, coord.Scope);
            coord.Tree.StopEnumeration();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TryGetSlideshowOverlayListPosition_tree_scope_matches_folder_scope_sibling_index()
    {
        var root = Path.Combine(Path.GetTempPath(), "ih_ov_" + Guid.NewGuid().ToString("N"));
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

            await coord.ToggleScopeAsync(fs, first);
            Assert.True(coord.TryGetSlideshowOverlayListPosition(out var folderIdx, out var folderTotal, out var folderDone));
            Assert.Equal(sortedIndex, folderIdx);
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
