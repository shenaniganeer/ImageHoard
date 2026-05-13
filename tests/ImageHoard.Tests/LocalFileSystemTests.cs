using ImageHoard.Core.Browse;
using ImageHoard.Core.Services;

namespace ImageHoard.Tests;

public sealed class LocalFileSystemTests
{    [Fact]
    public async Task ListDirectoryAsync_orders_directories_before_files_name_ascending()
    {
        var root = Path.Combine(Path.GetTempPath(), "ImageHoardTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "b_dir"));
        Directory.CreateDirectory(Path.Combine(root, "a_dir"));
        await File.WriteAllTextAsync(Path.Combine(root, "z.txt"), "x");
        await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "y");

        try
        {
            var fs = new LocalFileSystem();
            var list = await fs.ListDirectoryAsync(root);

            Assert.Equal(4, list.Count);
            Assert.True(list[0].IsDirectory && list[0].Name == "a_dir");
            Assert.True(list[1].IsDirectory && list[1].Name == "b_dir");
            Assert.False(list[2].IsDirectory);
            Assert.False(list[3].IsDirectory);
            Assert.Equal("a.txt", list[2].Name);
            Assert.Equal("z.txt", list[3].Name);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ListDirectoryAsync_throws_when_missing()
    {
        var fs = new LocalFileSystem();
        var missing = Path.Combine(Path.GetTempPath(), "ImageHoardMissing_" + Guid.NewGuid().ToString("N"));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => fs.ListDirectoryAsync(missing));
    }

    [Fact]
    public async Task ListDirectoryAsync_returns_image_files_for_filtering()
    {
        var root = Path.Combine(Path.GetTempPath(), "ImageHoardImg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        await File.WriteAllTextAsync(Path.Combine(root, "a.jpg"), "x");
        await File.WriteAllTextAsync(Path.Combine(root, "b.PNG"), "y");
        await File.WriteAllTextAsync(Path.Combine(root, "readme.txt"), "z");

        try
        {
            var fs = new LocalFileSystem();
            var list = await fs.ListDirectoryAsync(root);
            var images = list.Where(e => !e.IsDirectory && ImageExtensions.IsImageFile(e.FullPath)).ToList();

            Assert.Equal(2, images.Count);
            Assert.Contains(images, e => string.Equals(e.Name, "a.jpg", StringComparison.Ordinal));
            Assert.Contains(images, e => string.Equals(e.Name, "b.PNG", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ListDirectoryAsync_matches_extended_prefix_entry_count()
    {
        var root = Path.Combine(Path.GetTempPath(), "ImageHoardIoParity_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "d"));
        await File.WriteAllTextAsync(Path.Combine(root, "f.txt"), "x");

        try
        {
            var fs = new LocalFileSystem();
            var list = await fs.ListDirectoryAsync(root);
            var ioPath = PathNormalizer.ForIo(Path.GetFullPath(root));
            var viaIo = new DirectoryInfo(ioPath).EnumerateFileSystemInfos().ToArray();

            Assert.Equal(viaIo.Length, list.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MoveDirectoryAsync_same_volume_nested_tree_with_non_image_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "ImageHoardMoveDir_" + Guid.NewGuid().ToString("N"));
        var src = Path.Combine(root, "MoveMe");
        var nested = Path.Combine(src, "nested");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(Path.Combine(nested, "inner.txt"), "inner");
        await File.WriteAllTextAsync(Path.Combine(src, "readme.txt"), "not an image");

        var archiveParent = Path.Combine(root, "archive");
        Directory.CreateDirectory(archiveParent);
        var dest = Path.Combine(archiveParent, "MoveMe");

        try
        {
            var fs = new LocalFileSystem();
            await fs.MoveDirectoryAsync(src, dest);

            Assert.False(Directory.Exists(src));
            Assert.True(Directory.Exists(dest));
            Assert.Equal("inner", await File.ReadAllTextAsync(Path.Combine(dest, "nested", "inner.txt")));
            Assert.Equal("not an image", await File.ReadAllTextAsync(Path.Combine(dest, "readme.txt")));
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
    public async Task MoveDirectoryAsync_cross_volume_when_second_ready_drive_exists()
    {
        // Single-volume CI agents skip the body; dev machines with a second partition/drive get coverage.
        if (!TryGetOtherReadyDriveRoot(out var otherDriveRoot))
            return;

        var rootLocal = Path.Combine(Path.GetTempPath(), "ImageHoardMoveCross_" + Guid.NewGuid().ToString("N"));
        var src = Path.Combine(rootLocal, "MoveMe");
        var nested = Path.Combine(src, "nested");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(Path.Combine(nested, "inner.txt"), "inner");
        await File.WriteAllTextAsync(Path.Combine(src, "readme.txt"), "not an image");

        var rootRemote = Path.Combine(otherDriveRoot, "ImageHoardMoveCross_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootRemote);
        var dest = Path.Combine(rootRemote, "MoveMe");

        try
        {
            var fs = new LocalFileSystem();
            await fs.MoveDirectoryAsync(src, dest);

            Assert.False(Directory.Exists(src));
            Assert.True(Directory.Exists(dest));
            Assert.Equal("inner", await File.ReadAllTextAsync(Path.Combine(dest, "nested", "inner.txt")));
            Assert.Equal("not an image", await File.ReadAllTextAsync(Path.Combine(dest, "readme.txt")));
        }
        finally
        {
            try
            {
                Directory.Delete(rootLocal, recursive: true);
            }
            catch
            {
                // ignored
            }

            try
            {
                Directory.Delete(rootRemote, recursive: true);
            }
            catch
            {
                // ignored
            }
        }
    }

    private static bool TryGetOtherReadyDriveRoot(out string otherDriveRoot)
    {
        string tempRoot;
        try
        {
            tempRoot = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()))!;
        }
        catch
        {
            otherDriveRoot = string.Empty;
            return false;
        }

        foreach (var d in DriveInfo.GetDrives())
        {
            if (!d.IsReady)
                continue;

            string driveRoot;
            try
            {
                driveRoot = Path.GetPathRoot(Path.GetFullPath(d.Name))!;
            }
            catch
            {
                continue;
            }

            if (string.Equals(driveRoot, tempRoot, StringComparison.OrdinalIgnoreCase))
                continue;

            // Must include trailing '\' so Path.Combine("G:\", "x") is G:\x — Path.Combine("G:", "x") is wrong (relative path).
            var root = d.RootDirectory.FullName;
            try
            {
                var probe = Path.Combine(root, "ImageHoardDriveProbe_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(probe);
                Directory.Delete(probe);
            }
            catch
            {
                // Some letters report Ready but cannot host a new folder at the volume root (empty card readers, etc.).
                continue;
            }

            otherDriveRoot = root;
            return true;
        }

        otherDriveRoot = string.Empty;
        return false;
    }
}
