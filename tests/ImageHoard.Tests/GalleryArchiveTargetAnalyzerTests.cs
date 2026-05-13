using ImageHoard.Core.Services;

namespace ImageHoard.Tests;

public sealed class GalleryArchiveTargetAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_detects_identical_top_level_file_overlap()
    {
        var root = Path.Combine(Path.GetTempPath(), "ImageHoardGalPrev_" + Guid.NewGuid().ToString("N"));
        var work = Path.Combine(root, "Gallery");
        Directory.CreateDirectory(work);
        var archive = Path.Combine(root, "archive");
        Directory.CreateDirectory(archive);
        var dest = Path.Combine(archive, "Gallery");
        Directory.CreateDirectory(dest);

        var bytes = "hello-identical";
        await File.WriteAllTextAsync(Path.Combine(work, "a.jpg"), bytes);
        await File.WriteAllTextAsync(Path.Combine(dest, "a.jpg"), bytes);

        try
        {
            var fs = new LocalFileSystem();
            var p = await GalleryArchiveTargetAnalyzer.AnalyzeAsync(fs, archive, work);
            Assert.True(p.DestExists);
            Assert.False(p.SourceHasImmediateSubfolders);
            Assert.True(p.HasIdenticalFileOverlap);
            Assert.False(p.HasContentConflict);
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
    public async Task AnalyzeAsync_detects_content_conflict_at_top_level()
    {
        var root = Path.Combine(Path.GetTempPath(), "ImageHoardGalConflict_" + Guid.NewGuid().ToString("N"));
        var work = Path.Combine(root, "Gallery");
        Directory.CreateDirectory(work);
        var archive = Path.Combine(root, "archive");
        Directory.CreateDirectory(archive);
        var dest = Path.Combine(archive, "Gallery");
        Directory.CreateDirectory(dest);

        await File.WriteAllTextAsync(Path.Combine(work, "a.jpg"), "aaa");
        await File.WriteAllTextAsync(Path.Combine(dest, "a.jpg"), "bbb");

        try
        {
            var fs = new LocalFileSystem();
            var p = await GalleryArchiveTargetAnalyzer.AnalyzeAsync(fs, archive, work);
            Assert.True(p.DestExists);
            Assert.False(p.SourceHasImmediateSubfolders);
            Assert.False(p.HasIdenticalFileOverlap);
            Assert.True(p.HasContentConflict);
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
    public async Task AnalyzeAsync_sets_subfolder_flag_and_skips_collision_fields()
    {
        var root = Path.Combine(Path.GetTempPath(), "ImageHoardGalSub_" + Guid.NewGuid().ToString("N"));
        var work = Path.Combine(root, "Gallery");
        Directory.CreateDirectory(work);
        Directory.CreateDirectory(Path.Combine(work, "nested"));
        var archive = Path.Combine(root, "archive");
        Directory.CreateDirectory(archive);
        var dest = Path.Combine(archive, "Gallery");
        Directory.CreateDirectory(dest);
        await File.WriteAllTextAsync(Path.Combine(dest, "a.jpg"), "x");

        try
        {
            var fs = new LocalFileSystem();
            var p = await GalleryArchiveTargetAnalyzer.AnalyzeAsync(fs, archive, work);
            Assert.True(p.DestExists);
            Assert.True(p.SourceHasImmediateSubfolders);
            Assert.False(p.HasIdenticalFileOverlap);
            Assert.False(p.HasContentConflict);
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
