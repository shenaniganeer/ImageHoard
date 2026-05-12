using ImageHoard.Core.Browse;

namespace ImageHoard.Tests;

/// <summary>
/// Manual (WinUI): open a folder with many images, hold next or previous. Each keypress should enqueue one preview step;
/// tree selection may trail slightly but images should not jump ahead until catch-up lag (seconds greater than zero) allows coalescing.
/// </summary>
public sealed class BrowseSequentialNavIndexTests
{
    private static readonly string[] Paths = { @"C:\a.png", @"C:\b.png", @"C:\c.png" };

    [Fact]
    public void ComputeTargetIndexForStep_First_AlwaysZero()
    {
        var i = BrowseSequentialNavIndex.ComputeTargetIndexForStep(
            Paths,
            browseNavAnchorPath: @"C:\b.png",
            treeSelectedImagePath: null,
            displayedImagePath: null,
            BrowseNavStepKind.First);
        Assert.Equal(0, i);
    }

    [Fact]
    public void ComputeTargetIndexForStep_Last_AlwaysLast()
    {
        var i = BrowseSequentialNavIndex.ComputeTargetIndexForStep(
            Paths,
            browseNavAnchorPath: @"C:\b.png",
            treeSelectedImagePath: null,
            displayedImagePath: null,
            BrowseNavStepKind.Last);
        Assert.Equal(2, i);
    }

    [Fact]
    public void ComputeTargetIndexForStep_Next_UsesAnchorPrecedence()
    {
        var i = BrowseSequentialNavIndex.ComputeTargetIndexForStep(
            Paths,
            browseNavAnchorPath: @"C:\a.png",
            treeSelectedImagePath: @"C:\c.png",
            displayedImagePath: @"C:\c.png",
            BrowseNavStepKind.Next);
        Assert.Equal(1, i);
    }

    [Fact]
    public void ComputeTargetIndexForStep_Next_AtEnd_StaysAtEnd()
    {
        var i = BrowseSequentialNavIndex.ComputeTargetIndexForStep(
            Paths,
            browseNavAnchorPath: @"C:\c.png",
            treeSelectedImagePath: null,
            displayedImagePath: null,
            BrowseNavStepKind.Next);
        Assert.Equal(2, i);
    }

    [Fact]
    public void ComputeTargetIndexForStep_Previous_FromMiddle()
    {
        var i = BrowseSequentialNavIndex.ComputeTargetIndexForStep(
            Paths,
            browseNavAnchorPath: @"C:\b.png",
            treeSelectedImagePath: null,
            displayedImagePath: null,
            BrowseNavStepKind.Previous);
        Assert.Equal(0, i);
    }

    [Fact]
    public void ComputeTargetIndexForStep_Previous_AtStart_StaysAtStart()
    {
        var i = BrowseSequentialNavIndex.ComputeTargetIndexForStep(
            Paths,
            browseNavAnchorPath: @"C:\a.png",
            treeSelectedImagePath: null,
            displayedImagePath: null,
            BrowseNavStepKind.Previous);
        Assert.Equal(0, i);
    }

    [Fact]
    public void ComputeTargetIndexForStep_EmptyList_ReturnsMinusOne()
    {
        var i = BrowseSequentialNavIndex.ComputeTargetIndexForStep(
            Array.Empty<string>(),
            null,
            null,
            null,
            BrowseNavStepKind.Next);
        Assert.Equal(-1, i);
    }
}
