namespace ImageHoard.App;

/// <summary>Hotkeys editor grouping (Preferences → Hotkeys).</summary>
internal enum HotkeySection
{
    NavigationBrowseAndSort,
    Slideshow,
    Browse,
    View,
    Settings,
    Other,
}

/// <summary>Command registry rows for Hotkeys UI (FR-IN-01 + shipped profile extras).</summary>
internal static class CommandCatalog
{
    public sealed record Entry(string CommandId, string Description, bool AllowUserBinding, HotkeySection Section);

    public static IReadOnlyList<Entry> All { get; } =
    [
        new("nav.nextImage", "Next image", true, HotkeySection.NavigationBrowseAndSort),
        new("nav.prevImage", "Previous image", true, HotkeySection.NavigationBrowseAndSort),
        new("nav.firstImage", "First image in list", true, HotkeySection.NavigationBrowseAndSort),
        new("nav.lastImage", "Last image in list", true, HotkeySection.NavigationBrowseAndSort),
        new("nav.nextDirectory", "Next sibling folder", true, HotkeySection.NavigationBrowseAndSort),
        new("nav.prevDirectory", "Previous sibling folder", true, HotkeySection.NavigationBrowseAndSort),
        new("nav.cycleNavigationMode", "Cycle browse navigation mode", true, HotkeySection.NavigationBrowseAndSort),
        new("slideshow.start", "Start or resume slideshow", true, HotkeySection.NavigationBrowseAndSort),
        new("sort.flagKeep", "Sort: Flag Keep", true, HotkeySection.NavigationBrowseAndSort),
        new("sort.flagDelete", "Sort: Flag Delete", true, HotkeySection.NavigationBrowseAndSort),
        new("sort.flagUnset", "Sort: Flag Unset", true, HotkeySection.NavigationBrowseAndSort),
        new("sort.deleteArchiveWizard", "Call delete/archive wizard", true, HotkeySection.NavigationBrowseAndSort),
        new("sort.clearAllFlags", "Sort: Clear all flags", true, HotkeySection.NavigationBrowseAndSort),
        new("slideshow.siblingNextImage", "Slideshow: Next sibling image", true, HotkeySection.Slideshow),
        new("slideshow.siblingPrevImage", "Slideshow: Previous sibling image", true, HotkeySection.Slideshow),
        new("slideshow.switchToBrowseAtCurrentLocation", "Slideshow: Switch to browse mode at current location", true, HotkeySection.Slideshow),
        new("slideshow.nextTreeImage", "Slideshow: Next image", true, HotkeySection.Slideshow),
        new("slideshow.prevTreeImage", "Slideshow: Previous image", true, HotkeySection.Slideshow),
        new("slideshow.deleteCurrent", "Slideshow: Delete current image", true, HotkeySection.Slideshow),
        new("browse.toggleSubfolderInclusion", "Browse: Toggle subfolcer contents in image list", true, HotkeySection.Browse),
        new("browse.openGoToPath", "Browse: open Go to path dialog", true, HotkeySection.Browse),
        new("browse.openBookmarks", "Browse: open favorites", true, HotkeySection.Browse),
        new("browse.addBookmark", "Browse: add folder to favorites", true, HotkeySection.Browse),
        new("browse.revealInExplorer", "Browse: reveal in Explorer", true, HotkeySection.Browse),
        new("browse.findInTree", "Browse: find in folder tree", true, HotkeySection.Browse),
        new("browse.treeNext", "Browse: tree — next row (folders and images)", true, HotkeySection.Browse),
        new("browse.treePrevious", "Browse: tree — previous row (folders and images)", true, HotkeySection.Browse),
        new("browse.treeExpand", "Browse: tree — expand current folder", true, HotkeySection.Browse),
        new("browse.treeCollapse", "Browse: tree — collapse current folder", true, HotkeySection.Browse),
        new("browse.treeDelete", "Browse: tree — delete selected item(s) to Recycle Bin", true, HotkeySection.Browse),
        new("browse2.refreshTree", "Browse2: refresh folder tree listings (current + expanded)", true, HotkeySection.Browse),
        new("browse2.toggleImagePaneSubtreeRecursion", "Browse2: toggle include subfolders in image list", true, HotkeySection.Browse),
        new("view.cycleFitMode", "View: cycle shrink only / shrink & stretch / 1:1", true, HotkeySection.View),
        new("view.clearSelection", "View: clear image selection and preview", true, HotkeySection.View),
        new("view.panPreview", "View: pan primary preview (hold chord and drag)", true, HotkeySection.View),
        new("view.zoomIn", "View: zoom in", true, HotkeySection.View),
        new("view.zoomOut", "View: zoom out", true, HotkeySection.View),
        new("view.zoomResetFit", "View: zoom reset to default fit", true, HotkeySection.View),
        new("view.zoomActualPixels", "View: zoom to original file resolution (1:1)", true, HotkeySection.View),
        new("settings.open", "Settings: open settings", true, HotkeySection.Settings),
        new("settings.clearCaches", "Settings: clear caches", true, HotkeySection.Settings),
        new("ui.fullscreen", "Toggle fullscreen", true, HotkeySection.Other),
        new("ui.escape", "Exit fullscreen; clear preview when not fullscreen", true, HotkeySection.Other),
    ];

    public static ReadOnlySpan<HotkeySection> SectionDisplayOrder =>
    [
        HotkeySection.NavigationBrowseAndSort,
        HotkeySection.Slideshow,
        HotkeySection.Browse,
        HotkeySection.View,
        HotkeySection.Settings,
        HotkeySection.Other,
    ];

    public static string SectionHeader(HotkeySection section) =>
        section switch
        {
            HotkeySection.NavigationBrowseAndSort => "Navigation: Browse & Sort Mode",
            HotkeySection.Slideshow => "Navigation: Slideshow Mode",
            HotkeySection.Browse => "Browse",
            HotkeySection.View => "View",
            HotkeySection.Settings => "Settings",
            HotkeySection.Other => "Other",
            _ => section.ToString(),
        };
}
