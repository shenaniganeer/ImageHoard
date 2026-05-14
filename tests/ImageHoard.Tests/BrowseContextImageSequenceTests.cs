using ImageHoard.Core.Browse;
using ImageHoard.Core.Models;
using ImageHoard.Core.Sort;

namespace ImageHoard.Tests;

public sealed class BrowseContextImageSequenceTests
{
    [Fact]
    public void IsContextDirectoryUnderBrowseRoot_accepts_root_and_descendants()
    {
        var root = Path.Combine(Path.GetTempPath(), "ihBrowseRoot_" + Guid.NewGuid().ToString("n"));
        var sub = Path.Combine(root, "a", "b");
        Directory.CreateDirectory(sub);

        try
        {
            Assert.True(BrowseContextImageSequence.IsContextDirectoryUnderBrowseRoot(root, root));
            Assert.True(BrowseContextImageSequence.IsContextDirectoryUnderBrowseRoot(root, sub));
            Assert.False(BrowseContextImageSequence.IsContextDirectoryUnderBrowseRoot(sub, root));
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
    public void PickImmediateImageFiles_skips_directories_and_non_images()
    {
        var entries = new List<FileSystemEntry>
        {
            new(@"C:\x\a.jpg", "a.jpg", IsDirectory: false, 1, DateTimeOffset.UtcNow),
            new(@"C:\x\readme.txt", "readme.txt", IsDirectory: false, 1, DateTimeOffset.UtcNow),
            new(@"C:\x\sub", "sub", IsDirectory: true, null, null),
        };

        var picked = BrowseContextImageSequence.PickImmediateImageFiles(entries);
        Assert.Single(picked);
        Assert.EndsWith("a.jpg", picked[0].FullPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OrderImageFileEntries_NameNatural_sorts_display_names()
    {
        var t = DateTimeOffset.UtcNow;
        var entries = new List<FileSystemEntry>
        {
            new(@"C:\x\img10.png", "img10.png", false, 1, t),
            new(@"C:\x\img2.png", "img2.png", false, 1, t),
            new(@"C:\x\img1.png", "img1.png", false, 1, t),
        };

        var ordered = BrowseContextImageSequence.OrderImageFileEntries(entries, BrowseImageListSortKind.NameNatural);
        Assert.Equal(new[] { "img1.png", "img2.png", "img10.png" }, ordered.Select(e => e.Name));
    }

    [Fact]
    public void FilterPathsByNavigationMode_respects_flags()
    {
        var t = DateTimeOffset.UtcNow;
        var ordered = new List<FileSystemEntry>
        {
            new(@"C:\x\a.jpg", "a.jpg", false, 1, t),
            new(@"C:\x\b.jpg", "b.jpg", false, 1, t),
        };

        SortFlagState State(string p) =>
            string.Equals(p, @"C:\x\a.jpg", StringComparison.OrdinalIgnoreCase) ? SortFlagState.Keep : SortFlagState.Delete;

        var keepOnly = BrowseContextImageSequence.FilterPathsByNavigationMode(
            ordered,
            BrowseNavigationMode.KeepOnly,
            State);
        Assert.Single(keepOnly);
        Assert.Equal(@"C:\x\a.jpg", keepOnly[0], StringComparer.OrdinalIgnoreCase);

        var all = BrowseContextImageSequence.FilterPathsByNavigationMode(
            ordered,
            BrowseNavigationMode.AllImages,
            State);
        Assert.Equal(2, all.Count);
    }
}
