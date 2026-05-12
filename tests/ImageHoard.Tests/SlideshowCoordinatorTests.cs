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
}
