using ImageHoard.Core.Browse2;

namespace ImageHoard.Tests;

public sealed class FsMapDiffImagePaneScopeTests
{
    private const string Root = @"C:\Archive";
    private const string Sub = @"C:\Archive\Photos";

    [Fact]
    public void ImmediateMode_RefreshedSameFolder_Touches()
    {
        var d = new FsFolderRefreshedDiff(Root, Sub, null, new FsMapEntry());
        Assert.True(FsMapDiffImagePaneScope.TouchesImageList(Sub, includeSubfolders: false, d));
    }

    [Fact]
    public void ImmediateMode_RefreshedChildFolder_DoesNotTouch()
    {
        var child = @"C:\Archive\Photos\2024";
        var d = new FsFolderRefreshedDiff(Root, child, null, new FsMapEntry());
        Assert.False(FsMapDiffImagePaneScope.TouchesImageList(Sub, includeSubfolders: false, d));
    }

    [Fact]
    public void RecursiveMode_RefreshedChild_Touches()
    {
        var child = @"C:\Archive\Photos\2024";
        var d = new FsFolderRefreshedDiff(Root, child, null, new FsMapEntry());
        Assert.True(FsMapDiffImagePaneScope.TouchesImageList(Sub, includeSubfolders: true, d));
    }

    [Fact]
    public void ImmediateMode_RenameOfCurrentFolder_Touches()
    {
        var next = @"C:\Archive\PhotosRenamed";
        var d = new FsFolderRenamedDiff(Root, Sub, next, Root, Root);
        Assert.True(FsMapDiffImagePaneScope.TouchesImageList(Sub, includeSubfolders: false, d));
        Assert.True(FsMapDiffImagePaneScope.TouchesImageList(next, includeSubfolders: false, d));
    }

    [Fact]
    public void RecursiveMode_RenameInsideSubtree_Touches()
    {
        var oldP = @"C:\Archive\Photos\A";
        var newP = @"C:\Archive\Photos\B";
        var d = new FsFolderRenamedDiff(Root, oldP, newP, Sub, Sub);
        Assert.True(FsMapDiffImagePaneScope.TouchesImageList(Sub, includeSubfolders: true, d));
    }
}
