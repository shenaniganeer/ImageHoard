using ImageHoard.Core.Services;

namespace ImageHoard.Tests;

public sealed class RecursiveImageEnumeratorTests
{
    [Fact]
    public async Task EnumerateAsync_without_shuffle_depth_first_prefers_alphabetically_first_heavy_subtree()
    {
        var root = Path.Combine(Path.GetTempPath(), "ih_rie_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "a_heavy"));
        for (var i = 0; i < 5; i++)
            await File.WriteAllTextAsync(Path.Combine(root, "a_heavy", $"{i}.jpg"), "x");

        Directory.CreateDirectory(Path.Combine(root, "z_light"));
        await File.WriteAllTextAsync(Path.Combine(root, "z_light", "0.jpg"), "y");

        try
        {
            var fs = new LocalFileSystem();
            var first = await FirstEnumeratedPathAsync(fs, root, shuffleChildren: null).ConfigureAwait(true);
            Assert.NotNull(first);
            Assert.Contains("a_heavy", first!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("z_light", first!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EnumerateAsync_with_shuffle_some_seed_emits_z_light_before_exhausting_a_heavy()
    {
        var root = Path.Combine(Path.GetTempPath(), "ih_rie2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "a_heavy"));
        for (var i = 0; i < 5; i++)
            await File.WriteAllTextAsync(Path.Combine(root, "a_heavy", $"{i}.jpg"), "x");

        Directory.CreateDirectory(Path.Combine(root, "z_light"));
        await File.WriteAllTextAsync(Path.Combine(root, "z_light", "0.jpg"), "y");

        try
        {
            var fs = new LocalFileSystem();
            var found = false;
            for (var seed = 0; seed < 256; seed++)
            {
                var first = await FirstEnumeratedPathAsync(fs, root, new Random(seed)).ConfigureAwait(true);
                if (first != null && first.Contains("z_light", StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }

            Assert.True(found, "Expected some RNG seed to place z_light before a_heavy in DFS child order.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string?> FirstEnumeratedPathAsync(IFileSystem fs, string root, Random? shuffleChildren)
    {
        if (shuffleChildren == null)
        {
            await foreach (var path in RecursiveImageEnumerator.EnumerateAsync(fs, root, default).ConfigureAwait(true))
                return path;
        }
        else
        {
            await foreach (var path in RecursiveImageEnumerator.EnumerateAsync(fs, root, shuffleChildren, default)
                               .ConfigureAwait(true))
                return path;
        }

        return null;
    }
}
