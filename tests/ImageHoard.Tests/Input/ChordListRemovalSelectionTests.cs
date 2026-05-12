using ImageHoard.Core.Input;

namespace ImageHoard.Tests.Input;

public class ChordListRemovalSelectionTests
{
    /// <summary>Two variants "aa" and "bb" with a 3-char gap (like " · "). Total length 7.</summary>
    private static readonly List<(int Start, int EndExclusive)> TwoWithGap = [(0, 2), (5, 7)];

    [Fact]
    public void EmptyRanges_ReturnsEmpty()
    {
        var r = ChordListRemovalSelection.GetVariantIndicesToRemove(
            Array.Empty<(int, int)>(), 0, 0, 0, isBackspace: true);
        Assert.Empty(r);
    }

    [Fact]
    public void SelectionSpanningBothVariants_RemovesBothDescending()
    {
        var r = ChordListRemovalSelection.GetVariantIndicesToRemove(TwoWithGap, 7, 1, 5, isBackspace: false);
        Assert.Equal(new[] { 1, 0 }, r);
    }

    [Fact]
    public void BackspaceAtCaretZero_NoRemoval()
    {
        var r = ChordListRemovalSelection.GetVariantIndicesToRemove(TwoWithGap, 7, 0, 0, isBackspace: true);
        Assert.Empty(r);
    }

    [Fact]
    public void DeleteAtEnd_RemovesLastVariant()
    {
        var r = ChordListRemovalSelection.GetVariantIndicesToRemove(TwoWithGap, 7, 7, 0, isBackspace: false);
        Assert.Equal(new[] { 1 }, r);
    }

    [Fact]
    public void BackspaceInGap_RemovesLeftVariant()
    {
        var r = ChordListRemovalSelection.GetVariantIndicesToRemove(TwoWithGap, 7, 4, 0, isBackspace: true);
        Assert.Equal(new[] { 0 }, r);
    }

    [Fact]
    public void DeleteInGap_RemovesRightVariant()
    {
        var r = ChordListRemovalSelection.GetVariantIndicesToRemove(TwoWithGap, 7, 3, 0, isBackspace: false);
        Assert.Equal(new[] { 1 }, r);
    }

    [Fact]
    public void SelectionInsideFirstVariant_RemovesFirstOnly()
    {
        var r = ChordListRemovalSelection.GetVariantIndicesToRemove(TwoWithGap, 7, 0, 1, isBackspace: false);
        Assert.Equal(new[] { 0 }, r);
    }
}
