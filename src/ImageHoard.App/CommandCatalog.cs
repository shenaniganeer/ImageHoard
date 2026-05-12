namespace ImageHoard.App;

/// <summary>Command registry rows for Hotkeys UI (FR-IN-01 + shipped profile extras).</summary>
internal static class CommandCatalog
{
    public sealed record Entry(string CommandId, string Description, bool AllowUserBinding);

    public static IReadOnlyList<Entry> All { get; } =
    [
        new("nav.nextImage", "Next image", true),
        new("nav.prevImage", "Previous image", true),
        new("nav.firstImage", "First image in folder list", true),
        new("nav.lastImage", "Last image in folder list", true),
        new("nav.firstImage", "First image in folder list", true),
        new("nav.lastImage", "Last image in folder list", true),
        new("sort.flagKeep", "Sort: flag Keep", true),
        new("sort.flagDelete", "Sort: flag Delete", true),
        new("sort.flagUnset", "Sort: flag Unset", true),
        new("sort.commitBatchDelete", "Sort: batch delete flow", true),
        new("sort.moveToArchive", "Sort: move to archive wizard", true),
        new("sort.undoLastFlag", "Sort: undo last flag", true),
        new("slideshow.toggleScope", "Slideshow: toggle tree / folder scope", true),
        new("slideshow.reshuffle", "Slideshow: reshuffle session", true),
        new("slideshow.skipUnsupported", "Slideshow: skip unsupported file", true),
        new("slideshow.deleteCurrent", "Slideshow: delete current slide", true),
        new("browse.toggleSubfolderInclusion", "Browse: toggle include subfolders in list", true),
        new("browse.openGoToPath", "Browse: open Go to path dialog", true),
        new("browse.openBookmarks", "Browse: open favorites", true),
        new("browse.addBookmark", "Browse: add current folder to favorites", true),
        new("browse.revealInExplorer", "Browse: reveal in Explorer", true),
        new("view.cycleFitMode", "View: cycle image fit mode", true),
        new("view.zoomIn", "View: zoom in", true),
        new("view.zoomOut", "View: zoom out", true),
        new("settings.open", "Settings: open settings", false),
        new("settings.clearCaches", "Settings: clear caches", true),
        new("ui.fullscreen", "Toggle fullscreen", true),
        new("ui.escape", "Exit fullscreen / cancel", true),
    ];
}
