using ImageHoard.Core.Browse;
using Xunit;

namespace ImageHoard.Tests;

public sealed class BrowserPaneMovePathValidationTests
{
    [Fact]
    public void GetBlockingReason_RejectsDropOntoSelf()
    {
        var d = @"C:\dst";
        var r = BrowserPaneMovePathValidation.GetBlockingReason(
            new[] { (@"C:\dst", true) },
            d);
        Assert.Equal("Cannot drop an item onto itself.", r);
    }

    [Fact]
    public void GetBlockingReason_RejectsAncestorAndDescendantFoldersTogether()
    {
        var parent = @"C:\root\parent";
        var child = @"C:\root\parent\child";
        var r = BrowserPaneMovePathValidation.GetBlockingReason(
            new[] { (parent, true), (child, true) },
            @"C:\other");
        Assert.NotNull(r);
    }

    [Fact]
    public void GetBlockingReason_AllowsIndependentFolders()
    {
        var r = BrowserPaneMovePathValidation.GetBlockingReason(
            new[]
            {
                (@"C:\a\f1", true),
                (@"C:\a\f2", true),
            },
            @"C:\b");
        Assert.Null(r);
    }

    [Fact]
    public void GetBlockingReason_RejectsMoveIntoSubtree()
    {
        var r = BrowserPaneMovePathValidation.GetBlockingReason(
            new[] { (@"C:\root\outer", true) },
            @"C:\root\outer\inner");
        Assert.Equal("Cannot move a folder into one of its subfolders.", r);
    }
}
