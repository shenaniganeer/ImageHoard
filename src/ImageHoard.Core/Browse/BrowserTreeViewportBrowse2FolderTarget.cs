namespace ImageHoard.Core.Browse;

/// <summary>
/// Resolves which folder path Browse2 should keep in the folder tree viewport for a <see cref="BrowserTreeViewportIntent"/>.
/// Cold boot remains handled directly in the host.
/// </summary>
public static class BrowserTreeViewportBrowse2FolderTarget
{
    /// <summary>Returns the folder whose tree row should be scrolled into view, or <see langword="null"/> when none.</summary>
    public static string? ResolveFolderPathForViewportScroll(BrowserTreeViewportIntent intent, BrowserPaneState state)
    {
        if (intent.Reason == BrowserTreeViewportReason.ColdBootRestore)
            return null;

        string? N(string? p) => string.IsNullOrEmpty(p) ? null : p;

        var path = intent.Reason switch
        {
            BrowserTreeViewportReason.ImageStep =>
                N(intent.SecondaryPath) ?? BrowserTreeViewportIntentResolver.GetPinPathAfterBrowseCommit(state),
            BrowserTreeViewportReason.KeyboardMove =>
                N(intent.SecondaryPath) ?? N(intent.PrimaryPath),
            BrowserTreeViewportReason.FindHitFile =>
                N(intent.SecondaryPath),
            BrowserTreeViewportReason.FindHitFolder =>
                N(intent.PrimaryPath),
            BrowserTreeViewportReason.SiblingFolderNavigation =>
                N(intent.PrimaryPath),
            BrowserTreeViewportReason.AfterWizardImageDeletes
            or BrowserTreeViewportReason.AfterWizardNavigateToParent
            or BrowserTreeViewportReason.AfterWizardUndo
            or BrowserTreeViewportReason.FolderNavigation
            or BrowserTreeViewportReason.RootPopulate =>
                BrowserTreeViewportIntentResolver.GetPinPathAfterBrowseCommit(state),
            _ => BrowserTreeViewportIntentResolver.GetPinPathAfterBrowseCommit(state),
        };

        return string.IsNullOrEmpty(path) ? BrowserTreeViewportIntentResolver.GetPinPathAfterBrowseCommit(state) : path;
    }
}
