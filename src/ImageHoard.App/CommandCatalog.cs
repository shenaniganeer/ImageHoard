namespace ImageHoard.App;

/// <summary>Hotkeys editor grouping (Preferences → Hotkeys).</summary>
internal enum HotkeySection
{
    Navigation,
    Sorting,
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
        new("nav.nextImage", "Next image", true, HotkeySection.Navigation),
        new("nav.prevImage", "Previous image", true, HotkeySection.Navigation),
        new("nav.firstImage", "First image in folder list", true, HotkeySection.Navigation),
        new("nav.lastImage", "Last image in folder list", true, HotkeySection.Navigation),
        new("sort.flagKeep", "Sort: flag Keep", true, HotkeySection.Sorting),
        new("sort.flagDelete", "Sort: flag Delete", true, HotkeySection.Sorting),
        new("sort.flagUnset", "Sort: flag Unset", true, HotkeySection.Sorting),
        new("sort.commitBatchDelete", "Sort: batch delete flow", true, HotkeySection.Sorting),
        new("sort.moveToArchive", "Sort: move to archive wizard", true, HotkeySection.Sorting),
        new("sort.undoLastFlag", "Sort: undo last flag", true, HotkeySection.Sorting),
        new("slideshow.start", "Slideshow: start from current folder (tree)", true, HotkeySection.Slideshow),
        new("slideshow.toggleScope", "Slideshow: toggle tree / folder scope", true, HotkeySection.Slideshow),
        new("slideshow.reshuffle", "Slideshow: reshuffle session", true, HotkeySection.Slideshow),
        new("slideshow.skipUnsupported", "Slideshow: skip unsupported file", true, HotkeySection.Slideshow),
        new("slideshow.deleteCurrent", "Slideshow: delete current slide", true, HotkeySection.Slideshow),
        new("browse.toggleSubfolderInclusion", "Browse: toggle include subfolders in list", true, HotkeySection.Browse),
        new("browse.openGoToPath", "Browse: open Go to path dialog", true, HotkeySection.Browse),
        new("browse.openBookmarks", "Browse: open favorites", true, HotkeySection.Browse),
        new("browse.addBookmark", "Browse: add folder to favorites", true, HotkeySection.Browse),
        new("browse.revealInExplorer", "Browse: reveal in Explorer", true, HotkeySection.Browse),
        new("view.cycleFitMode", "View: cycle image fit mode", true, HotkeySection.View),
        new("view.clearSelection", "View: clear image selection and preview", true, HotkeySection.View),
        new("view.panPreview", "View: pan primary preview (hold chord and drag)", true, HotkeySection.View),
        new("view.zoomIn", "View: zoom in", true, HotkeySection.View),
        new("view.zoomOut", "View: zoom out", true, HotkeySection.View),
        new("settings.open", "Settings: open settings", false, HotkeySection.Settings),
        new("settings.clearCaches", "Settings: clear caches", true, HotkeySection.Settings),
        new("ui.fullscreen", "Toggle fullscreen", true, HotkeySection.Other),
        new("ui.escape", "Exit fullscreen; clear preview when not fullscreen", true, HotkeySection.Other),
    ];

    public static ReadOnlySpan<HotkeySection> SectionDisplayOrder =>
    [
        HotkeySection.Navigation,
        HotkeySection.Sorting,
        HotkeySection.Slideshow,
        HotkeySection.Browse,
        HotkeySection.View,
        HotkeySection.Settings,
        HotkeySection.Other,
    ];

    public static string SectionHeader(HotkeySection section) =>
        section switch
        {
            HotkeySection.Navigation => "Navigation",
            HotkeySection.Sorting => "Sorting",
            HotkeySection.Slideshow => "Slideshow",
            HotkeySection.Browse => "Browse",
            HotkeySection.View => "View",
            HotkeySection.Settings => "Settings",
            HotkeySection.Other => "Other",
            _ => section.ToString(),
        };
}
