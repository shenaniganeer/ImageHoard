using ImageHoard.Core.Browse;

namespace ImageHoard.Tests;

public sealed class BrowseContextDirectoryTests
{
    [Fact]
    public void Resolve_UsesAnchorBeforeSelectionAndDisplayed()
    {
        var d = BrowseContextDirectory.Resolve(
            browseNavAnchorPath: @"C:\keep\a.png",
            treeSelectedImagePath: @"C:\sel\b.png",
            displayedImagePath: @"C:\disp\c.png",
            browseRootFolderPath: @"C:\root");

        Assert.Equal(@"C:\keep", d);
    }

    [Fact]
    public void Resolve_FallsBackToBrowseRootWhenNoFileHints()
    {
        var d = BrowseContextDirectory.Resolve(
            null,
            null,
            null,
            @"D:\pics");

        Assert.Equal(@"D:\pics", d);
    }

    [Fact]
    public void Resolve_UsesDisplayedWhenAnchorAndSelectionMissing()
    {
        var d = BrowseContextDirectory.Resolve(
            null,
            null,
            @"C:\only\here.png",
            @"C:\root");

        Assert.Equal(@"C:\only", d);
    }

    [Fact]
    public void Resolve_UsesTreeSelectedFolderWhenNoFileHintsAndFolderUnderBrowseRoot()
    {
        var d = BrowseContextDirectory.Resolve(
            null,
            null,
            null,
            @"C:\root",
            @"C:\root\child");

        Assert.Equal(@"C:\root\child", d);
    }

    [Fact]
    public void Resolve_FileHintsWinOverTreeSelectedFolder()
    {
        var d = BrowseContextDirectory.Resolve(
            browseNavAnchorPath: @"C:\root\other\a.png",
            treeSelectedImagePath: null,
            displayedImagePath: null,
            browseRootFolderPath: @"C:\root",
            treeSelectedFolderPath: @"C:\root\child");

        Assert.Equal(@"C:\root\other", d);
    }

    [Fact]
    public void Resolve_IgnoresTreeSelectedFolderNotUnderBrowseRoot()
    {
        var d = BrowseContextDirectory.Resolve(
            null,
            null,
            null,
            @"C:\root",
            @"D:\outside");

        Assert.Equal(@"C:\root", d);
    }
}
