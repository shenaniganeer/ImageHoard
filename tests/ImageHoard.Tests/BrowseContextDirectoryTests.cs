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
}
