using ImageHoard.Core.Browse;
using ImageHoard.Core.Services;

namespace ImageHoard.Tests;
public sealed class PathNormalizerTests
{
    [Fact]
    public void ForIo_prefixes_local_path()
    {
        var temp = Path.Combine(Path.GetTempPath(), "ih_norm_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            var full = Path.GetFullPath(temp);
            var io = PathNormalizer.ForIo(full);
            Assert.StartsWith(@"\\?\", io, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void ForDirectoryListing_does_not_add_extended_prefix()
    {
        var temp = Path.Combine(Path.GetTempPath(), "ih_listing_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            var full = Path.GetFullPath(temp);
            var listing = PathNormalizer.ForDirectoryListing(full);
            Assert.False(listing.StartsWith(@"\\?\", StringComparison.Ordinal), listing);
            Assert.Equal(full, listing);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }
}

public sealed class RecursiveImageEnumeratorTests
{
    [Fact]
    public async Task EnumerateAsync_finds_nested_images_depth_first_dirs_before_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "ImageHoardEnum_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "z_sub"));
        Directory.CreateDirectory(Path.Combine(root, "a_sub"));
        await File.WriteAllTextAsync(Path.Combine(root, "a_sub", "b.jpg"), "x");
        await File.WriteAllTextAsync(Path.Combine(root, "root.png"), "y");
        await File.WriteAllTextAsync(Path.Combine(root, "z_sub", "a.jpg"), "z");

        try
        {
            var fs = new LocalFileSystem();
            var list = new List<string>();
            await foreach (var p in RecursiveImageEnumerator.EnumerateAsync(fs, root))
                list.Add(Path.GetFileName(p));

            // Depth-first: a_sub then z_sub (name order), then root-level files
            Assert.Equal(["b.jpg", "a.jpg", "root.png"], list);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
