using ImageHoard.Core.Browse;

namespace ImageHoard.Tests;

public sealed class BrowseContextImageSequencePickNextTests
{
    [Fact]
    public void PickNext_after_single_middle_removal_returns_following_path()
    {
        var before = new[] { @"C:\a.png", @"C:\b.png", @"C:\c.png" };
        var removed = new[] { @"C:\b.png" };
        var next = BrowseContextImageSequence.PickNextDisplayedPathAfterRemovalsInOrderedList(before, removed);
        Assert.Equal(@"C:\c.png", next);
    }

    [Fact]
    public void PickNext_after_last_removal_returns_previous_path()
    {
        var before = new[] { @"C:\a.png", @"C:\b.png", @"C:\c.png" };
        var removed = new[] { @"C:\c.png" };
        var next = BrowseContextImageSequence.PickNextDisplayedPathAfterRemovalsInOrderedList(before, removed);
        Assert.Equal(@"C:\b.png", next);
    }

    [Fact]
    public void PickNext_after_contiguous_tail_removal_returns_last_remaining()
    {
        var before = new[] { @"C:\a.png", @"C:\b.png", @"C:\c.png", @"C:\d.png" };
        var removed = new[] { @"C:\c.png", @"C:\d.png" };
        var next = BrowseContextImageSequence.PickNextDisplayedPathAfterRemovalsInOrderedList(before, removed);
        Assert.Equal(@"C:\b.png", next);
    }

    [Fact]
    public void PickNext_after_first_removal_returns_second()
    {
        var before = new[] { @"C:\a.png", @"C:\b.png" };
        var removed = new[] { @"C:\a.png" };
        var next = BrowseContextImageSequence.PickNextDisplayedPathAfterRemovalsInOrderedList(before, removed);
        Assert.Equal(@"C:\b.png", next);
    }

    [Fact]
    public void PickNext_is_case_insensitive_for_removed_set()
    {
        var before = new[] { @"C:\a.png", @"C:\b.png" };
        var removed = new[] { @"c:\B.PNG" };
        var next = BrowseContextImageSequence.PickNextDisplayedPathAfterRemovalsInOrderedList(before, removed);
        Assert.Equal(@"C:\a.png", next);
    }

    [Fact]
    public void PickNext_when_all_removed_returns_null()
    {
        var before = new[] { @"C:\a.png" };
        var removed = new[] { @"C:\a.png" };
        Assert.Null(BrowseContextImageSequence.PickNextDisplayedPathAfterRemovalsInOrderedList(before, removed));
    }

    [Fact]
    public void PickNext_when_removed_not_in_list_returns_null()
    {
        var before = new[] { @"C:\a.png", @"C:\b.png" };
        var removed = new[] { @"C:\z.png" };
        Assert.Null(BrowseContextImageSequence.PickNextDisplayedPathAfterRemovalsInOrderedList(before, removed));
    }
}
