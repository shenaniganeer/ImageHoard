using ImageHoard.Core.Sort;

namespace ImageHoard.Core.Browse;

/// <summary>Restricts sequential browse navigation to a subset of images by sort flag.</summary>
public enum BrowseNavigationMode
{
    AllImages,
    KeepOnly,
    NotKeepOnly,
    UnflaggedOnly,
    DeleteOnly,
}

/// <summary>Predicate for <see cref="BrowseNavigationMode"/> against <see cref="SortFlagState"/>.</summary>
public static class BrowseNavigationModeFilter
{
    public static bool Matches(SortFlagState flag, BrowseNavigationMode mode) =>
        mode switch
        {
            BrowseNavigationMode.AllImages => true,
            BrowseNavigationMode.KeepOnly => flag == SortFlagState.Keep,
            BrowseNavigationMode.NotKeepOnly => flag != SortFlagState.Keep,
            BrowseNavigationMode.UnflaggedOnly => flag == SortFlagState.Unset,
            BrowseNavigationMode.DeleteOnly => flag == SortFlagState.Delete,
            _ => true,
        };

    /// <summary>Cycles: All → Keep → Not keep → Unflagged → Delete → All.</summary>
    public static BrowseNavigationMode CycleNext(BrowseNavigationMode current) =>
        current switch
        {
            BrowseNavigationMode.AllImages => BrowseNavigationMode.KeepOnly,
            BrowseNavigationMode.KeepOnly => BrowseNavigationMode.NotKeepOnly,
            BrowseNavigationMode.NotKeepOnly => BrowseNavigationMode.UnflaggedOnly,
            BrowseNavigationMode.UnflaggedOnly => BrowseNavigationMode.DeleteOnly,
            BrowseNavigationMode.DeleteOnly => BrowseNavigationMode.AllImages,
            _ => BrowseNavigationMode.AllImages,
        };
}
