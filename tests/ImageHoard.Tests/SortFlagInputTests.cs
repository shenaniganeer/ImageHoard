using ImageHoard.Core.Sort;

namespace ImageHoard.Tests;

public sealed class SortFlagInputTests
{
    [Theory]
    [InlineData(SortFlagState.Unset, SortFlagState.Keep, SortFlagState.Keep)]
    [InlineData(SortFlagState.Keep, SortFlagState.Keep, SortFlagState.Unset)]
    [InlineData(SortFlagState.Delete, SortFlagState.Keep, SortFlagState.Keep)]
    [InlineData(SortFlagState.Unset, SortFlagState.Delete, SortFlagState.Delete)]
    [InlineData(SortFlagState.Delete, SortFlagState.Delete, SortFlagState.Unset)]
    [InlineData(SortFlagState.Keep, SortFlagState.Delete, SortFlagState.Delete)]
    [InlineData(SortFlagState.Unset, SortFlagState.Unset, SortFlagState.Unset)]
    [InlineData(SortFlagState.Keep, SortFlagState.Unset, SortFlagState.Unset)]
    [InlineData(SortFlagState.Delete, SortFlagState.Unset, SortFlagState.Unset)]
    public void ResolveToggle_maps_current_and_requested(SortFlagState current, SortFlagState requested, SortFlagState expected) =>
        Assert.Equal(expected, SortFlagInput.ResolveToggle(current, requested));
}
