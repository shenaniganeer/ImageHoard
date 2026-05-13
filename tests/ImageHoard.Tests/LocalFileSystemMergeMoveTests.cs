using ImageHoard.Core.Services;

namespace ImageHoard.Tests;

public sealed class LocalFileSystemMergeMoveTests
{
    [Fact]
    public async Task MergeMoveDirectoryAsync_when_destination_missing_matches_MoveDirectoryAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "ImageHoardMergeA_" + Guid.NewGuid().ToString("N"));
        var src = Path.Combine(root, "MoveMe");
        Directory.CreateDirectory(src);
        await File.WriteAllTextAsync(Path.Combine(src, "a.txt"), "x");

        var archiveParent = Path.Combine(root, "archive");
        Directory.CreateDirectory(archiveParent);
        var dest = Path.Combine(archiveParent, "MoveMe");

        try
        {
            var fs = new LocalFileSystem();
            await fs.MergeMoveDirectoryAsync(src, dest);

            Assert.False(Directory.Exists(src));
            Assert.True(Directory.Exists(dest));
            Assert.Equal("x", await File.ReadAllTextAsync(Path.Combine(dest, "a.txt")));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public async Task MergeMoveDirectoryAsync_merges_when_destination_exists_no_name_overlap()
    {
        var root = Path.Combine(Path.GetTempPath(), "ImageHoardMergeB_" + Guid.NewGuid().ToString("N"));
        var src = Path.Combine(root, "G");
        Directory.CreateDirectory(src);
        await File.WriteAllTextAsync(Path.Combine(src, "from.txt"), "from");

        var dest = Path.Combine(root, "archive", "G");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        Directory.CreateDirectory(dest);
        await File.WriteAllTextAsync(Path.Combine(dest, "existing.txt"), "stay");

        try
        {
            var fs = new LocalFileSystem();
            await fs.MergeMoveDirectoryAsync(src, dest);

            Assert.False(Directory.Exists(src));
            Assert.Equal("stay", await File.ReadAllTextAsync(Path.Combine(dest, "existing.txt")));
            Assert.Equal("from", await File.ReadAllTextAsync(Path.Combine(dest, "from.txt")));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public async Task MergeMoveDirectoryAsync_identical_file_collision_removes_source_only()
    {
        var root = Path.Combine(Path.GetTempPath(), "ImageHoardMergeC_" + Guid.NewGuid().ToString("N"));
        var src = Path.Combine(root, "G");
        Directory.CreateDirectory(src);
        var dest = Path.Combine(root, "archive", "G");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        Directory.CreateDirectory(dest);

        var payload = "same-bytes-payload";
        await File.WriteAllTextAsync(Path.Combine(src, "dup.jpg"), payload);
        await File.WriteAllTextAsync(Path.Combine(dest, "dup.jpg"), payload);

        try
        {
            var fs = new LocalFileSystem();
            await fs.MergeMoveDirectoryAsync(src, dest);

            Assert.False(Directory.Exists(src));
            Assert.Equal(payload, await File.ReadAllTextAsync(Path.Combine(dest, "dup.jpg")));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public async Task MergeMoveDirectoryAsync_throws_when_colliding_files_differ()
    {
        var root = Path.Combine(Path.GetTempPath(), "ImageHoardMergeD_" + Guid.NewGuid().ToString("N"));
        var src = Path.Combine(root, "G");
        Directory.CreateDirectory(src);
        var dest = Path.Combine(root, "archive", "G");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        Directory.CreateDirectory(dest);

        await File.WriteAllTextAsync(Path.Combine(src, "dup.jpg"), "aaa");
        await File.WriteAllTextAsync(Path.Combine(dest, "dup.jpg"), "bbb");

        try
        {
            var fs = new LocalFileSystem();
            var ex = await Assert.ThrowsAsync<IOException>(() => fs.MergeMoveDirectoryAsync(src, dest));
            Assert.Contains("ImageHoard merge", ex.Message, StringComparison.Ordinal);
            Assert.True(Directory.Exists(src));
            Assert.Equal("aaa", await File.ReadAllTextAsync(Path.Combine(src, "dup.jpg")));
            Assert.Equal("bbb", await File.ReadAllTextAsync(Path.Combine(dest, "dup.jpg")));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // ignored
            }
        }
    }
}
