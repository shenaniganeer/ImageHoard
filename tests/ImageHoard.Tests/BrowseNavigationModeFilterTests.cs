using ImageHoard.Core.Browse;
using ImageHoard.Core.Sort;

namespace ImageHoard.Tests;

public sealed class BrowseNavigationModeFilterTests
{
    public static TheoryData<SortFlagState, BrowseNavigationMode, bool> MatchesCases =>
        new()
        {
            { SortFlagState.Unset, BrowseNavigationMode.AllImages, true },
            { SortFlagState.Keep, BrowseNavigationMode.AllImages, true },
            { SortFlagState.Delete, BrowseNavigationMode.AllImages, true },
            { SortFlagState.Unset, BrowseNavigationMode.KeepOnly, false },
            { SortFlagState.Keep, BrowseNavigationMode.KeepOnly, true },
            { SortFlagState.Delete, BrowseNavigationMode.KeepOnly, false },
            { SortFlagState.Unset, BrowseNavigationMode.NotKeepOnly, true },
            { SortFlagState.Keep, BrowseNavigationMode.NotKeepOnly, false },
            { SortFlagState.Delete, BrowseNavigationMode.NotKeepOnly, true },
            { SortFlagState.Unset, BrowseNavigationMode.UnflaggedOnly, true },
            { SortFlagState.Keep, BrowseNavigationMode.UnflaggedOnly, false },
            { SortFlagState.Delete, BrowseNavigationMode.UnflaggedOnly, false },
            { SortFlagState.Unset, BrowseNavigationMode.DeleteOnly, false },
            { SortFlagState.Keep, BrowseNavigationMode.DeleteOnly, false },
            { SortFlagState.Delete, BrowseNavigationMode.DeleteOnly, true },
        };

    [Theory]
    [MemberData(nameof(MatchesCases))]
    public void Matches_expected(SortFlagState flag, BrowseNavigationMode mode, bool expected) =>
        Assert.Equal(expected, BrowseNavigationModeFilter.Matches(flag, mode));

    [Fact]
    public void CycleNext_rotates_through_all_modes()
    {
        var m = BrowseNavigationMode.AllImages;
        m = BrowseNavigationModeFilter.CycleNext(m);
        Assert.Equal(BrowseNavigationMode.KeepOnly, m);
        m = BrowseNavigationModeFilter.CycleNext(m);
        Assert.Equal(BrowseNavigationMode.NotKeepOnly, m);
        m = BrowseNavigationModeFilter.CycleNext(m);
        Assert.Equal(BrowseNavigationMode.UnflaggedOnly, m);
        m = BrowseNavigationModeFilter.CycleNext(m);
        Assert.Equal(BrowseNavigationMode.DeleteOnly, m);
        m = BrowseNavigationModeFilter.CycleNext(m);
        Assert.Equal(BrowseNavigationMode.AllImages, m);
    }
}
