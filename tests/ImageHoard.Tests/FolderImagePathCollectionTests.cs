using ImageHoard.Core.Browse;
using ImageHoard.Core.Services;

namespace ImageHoard.Tests;

public sealed class FolderImagePathCollectionTests
{
    [Fact]
    public async Task CollectAsync_non_recursive_only_immediate_images()
    {
        var root = Path.Combine(Path.GetTempPath(), "ImageHoardFipc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        await File.WriteAllTextAsync(Path.Combine(root, "a.png"), "x");
        await File.WriteAllTextAsync(Path.Combine(root, "sub", "b.png"), "y");
        await File.WriteAllTextAsync(Path.Combine(root, "readme.txt"), "z");

        try
        {
            var fs = new LocalFileSystem();
            var paths = await FolderImagePathCollection.CollectAsync(fs, root, includeSubfolders: false);
            paths.Sort(StringComparer.OrdinalIgnoreCase);
            Assert.Single(paths);
            Assert.EndsWith("a.png", paths[0], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CollectAsync_recursive_includes_nested()
    {
        var root = Path.Combine(Path.GetTempPath(), "ImageHoardFipcR_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        await File.WriteAllTextAsync(Path.Combine(root, "a.png"), "x");
        await File.WriteAllTextAsync(Path.Combine(root, "sub", "b.png"), "y");

        try
        {
            var fs = new LocalFileSystem();
            var paths = await FolderImagePathCollection.CollectAsync(fs, root, includeSubfolders: true);
            Assert.Equal(2, paths.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
