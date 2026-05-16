using System.IO;

namespace ImageHoard.Core.Browse;

/// <summary>
/// Pure resolution of <see cref="BrowserTreeViewportIntent"/> from browse pane state (unit-tested; WinUI pump consumes results).
/// </summary>
public static class BrowserTreeViewportIntentResolver
{
    /// <summary>
    /// Same as <see cref="ForWizardCommit(BrowserPaneState, BrowserTreeViewportReason, BrowserTreeRefocusAfterWizardContext?)"/>
    /// with <see cref="BrowserTreeViewportReason.AfterWizardImageDeletes"/> (bulk image delete / refresh path).
    /// </summary>
    public static BrowserTreeViewportIntent ForWizardCommit(
        BrowserPaneState state,
        BrowserTreeRefocusAfterWizardContext? refocusContext = null) =>
        ForWizardCommit(state, BrowserTreeViewportReason.AfterWizardImageDeletes, refocusContext);

    /// <summary>
    /// Wizard completion paths match today's <c>GetBrowserTreeViewportPinPathAfterBrowseCommit()</c> pin
    /// (selection is already committed in the host). <paramref name="reason"/> must be a wizard reason.
    /// </summary>
    public static BrowserTreeViewportIntent ForWizardCommit(
        BrowserPaneState state,
        BrowserTreeViewportReason reason,
        BrowserTreeRefocusAfterWizardContext? refocusContext = null)
    {
        if (reason is not (BrowserTreeViewportReason.AfterWizardImageDeletes
            or BrowserTreeViewportReason.AfterWizardNavigateToParent
            or BrowserTreeViewportReason.AfterWizardUndo))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Expected a wizard viewport reason.");
        }

        _ = refocusContext;
        var pin = GetPinPathAfterBrowseCommit(state);
        return new BrowserTreeViewportIntent(reason, pin, null, 0.0, PreferSelectionFirst: false);
    }

    public static BrowserTreeViewportIntent ForSiblingFolderNav(BrowserPaneState state, string targetFolderPath) =>
        new(
            BrowserTreeViewportReason.SiblingFolderNavigation,
            NormalizePathOrSelf(targetFolderPath),
            null,
            0.0,
            PreferSelectionFirst: false);

    public static BrowserTreeViewportIntent ForFindHit(BrowserPaneState state, BrowserFindMatch match)
    {
        if (string.IsNullOrEmpty(state.CurrentFolderPath) || !Directory.Exists(state.CurrentFolderPath))
            return new BrowserTreeViewportIntent(BrowserTreeViewportReason.FindHitFolder, null, null, 0.0, true);

        if (!IsSameOrDescendantDirectory(state.CurrentFolderPath, match.Path))
        {
            var reason = match.Kind == BrowserFindMatchKind.Folder
                ? BrowserTreeViewportReason.FindHitFolder
                : BrowserTreeViewportReason.FindHitFile;
            return new BrowserTreeViewportIntent(reason, null, null, 0.0, PreferSelectionFirst: true);
        }

        if (match.Kind == BrowserFindMatchKind.Folder)
        {
            return new BrowserTreeViewportIntent(
                BrowserTreeViewportReason.FindHitFolder,
                NormalizePathOrSelf(match.Path),
                null,
                0.0,
                PreferSelectionFirst: true);
        }

        var parentDir = Path.GetDirectoryName(match.Path);
        var secondary = string.IsNullOrEmpty(parentDir) ? state.CurrentFolderPath : NormalizePathOrSelf(parentDir);
        return new BrowserTreeViewportIntent(
            BrowserTreeViewportReason.FindHitFile,
            NormalizePathOrSelf(match.Path),
            secondary,
            0.0,
            PreferSelectionFirst: true);
    }

    public static BrowserTreeViewportIntent ForImageStep(BrowserPaneState state, string nextImagePath)
    {
        var primary = NormalizePathOrSelf(nextImagePath);
        var parentDir = Path.GetDirectoryName(nextImagePath);
        var secondary = string.IsNullOrEmpty(parentDir)
            ? state.CurrentFolderPath
            : NormalizePathOrSelf(parentDir);
        return new BrowserTreeViewportIntent(
            BrowserTreeViewportReason.ImageStep,
            primary,
            secondary,
            0.0,
            PreferSelectionFirst: true,
            BrowserTreeViewportVisibility.PageWhenOutsideViewport);
    }

    public static BrowserTreeViewportIntent ForRootPopulate(BrowserPaneState state) =>
        new(BrowserTreeViewportReason.RootPopulate, null, null, 0.0, PreferSelectionFirst: false);

    /// <summary>
    /// Cold boot: scroll tree to top when <paramref name="lastActedFsObject"/> is null/empty/invalid/outside browse root;
    /// pin folder row to top when anchor is a directory; center file row when anchor is a file.
    /// </summary>
    public static BrowserTreeViewportIntent ForColdBootAnchor(BrowserPaneState state, string? lastActedFsObject)
    {
        if (string.IsNullOrWhiteSpace(lastActedFsObject)
            || string.IsNullOrEmpty(state.CurrentFolderPath)
            || !Directory.Exists(state.CurrentFolderPath))
        {
            return new BrowserTreeViewportIntent(
                BrowserTreeViewportReason.ColdBootRestore,
                null,
                null,
                0.0,
                PreferSelectionFirst: false);
        }

        string root;
        try
        {
            root = Path.GetFullPath(state.CurrentFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch
        {
            return new BrowserTreeViewportIntent(
                BrowserTreeViewportReason.ColdBootRestore,
                null,
                null,
                0.0,
                PreferSelectionFirst: false);
        }

        string anchorNorm;
        try
        {
            anchorNorm = Path.GetFullPath(lastActedFsObject.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch
        {
            return new BrowserTreeViewportIntent(
                BrowserTreeViewportReason.ColdBootRestore,
                null,
                null,
                0.0,
                PreferSelectionFirst: false);
        }

        if (!IsUnderBrowseRoot(root, anchorNorm))
        {
            return new BrowserTreeViewportIntent(
                BrowserTreeViewportReason.ColdBootRestore,
                null,
                null,
                0.0,
                PreferSelectionFirst: false);
        }

        if (Directory.Exists(anchorNorm))
        {
            return new BrowserTreeViewportIntent(
                BrowserTreeViewportReason.ColdBootRestore,
                anchorNorm,
                null,
                0.0,
                PreferSelectionFirst: false);
        }

        if (File.Exists(anchorNorm))
        {
            var parentDir = Path.GetDirectoryName(anchorNorm);
            var secondary = string.IsNullOrEmpty(parentDir)
                ? state.CurrentFolderPath
                : NormalizePathOrSelf(parentDir);
            return new BrowserTreeViewportIntent(
                BrowserTreeViewportReason.ColdBootRestore,
                anchorNorm,
                secondary,
                0.5,
                PreferSelectionFirst: true);
        }

        return new BrowserTreeViewportIntent(
            BrowserTreeViewportReason.ColdBootRestore,
            null,
            null,
            0.0,
            PreferSelectionFirst: false);
    }

    public static BrowserTreeViewportIntent ForKeyboardMove(BrowserPaneState state, string movedToNodePath)
    {
        var primary = NormalizePathOrSelf(movedToNodePath);
        string? secondary = null;
        if (!string.IsNullOrEmpty(Path.GetFileName(movedToNodePath)) && Path.HasExtension(movedToNodePath))
        {
            var parentDir = Path.GetDirectoryName(movedToNodePath);
            secondary = string.IsNullOrEmpty(parentDir)
                ? state.CurrentFolderPath
                : NormalizePathOrSelf(parentDir);
        }

        return new BrowserTreeViewportIntent(
            BrowserTreeViewportReason.KeyboardMove,
            primary,
            secondary,
            0.0,
            PreferSelectionFirst: true);
    }

    public static BrowserTreeViewportIntent ForFolderNavigation(BrowserPaneState state) =>
        new(
            BrowserTreeViewportReason.FolderNavigation,
            GetPinPathAfterBrowseCommit(state),
            null,
            0.0,
            PreferSelectionFirst: false);

    /// <summary>
    /// Same rules as WinUI <c>ResolveBrowsedFolderPathForBrowserTreeViewport</c> + <c>GetBrowserTreeViewportPinPathAfterBrowseCommit</c>.
    /// </summary>
    public static string? GetPinPathAfterBrowseCommit(BrowserPaneState state)
    {
        var browsed = ResolveBrowsedFolderPathForViewport(state);
        if (!string.IsNullOrEmpty(browsed) && Directory.Exists(browsed))
            return browsed;
        if (!string.IsNullOrEmpty(state.CurrentFolderPath) && Directory.Exists(state.CurrentFolderPath))
            return NormalizePathOrSelf(state.CurrentFolderPath);
        return null;
    }

    /// <summary>Same rules as WinUI <c>ResolveBrowsedFolderPathForBrowserTreeViewport</c>.</summary>
    public static string? ResolveBrowsedFolderPathForViewport(BrowserPaneState state)
    {
        if (string.IsNullOrEmpty(state.CurrentFolderPath) || !Directory.Exists(state.CurrentFolderPath))
            return null;

        var browsed = BrowseContextDirectory.Resolve(
            state.BrowseNavAnchorPath,
            state.TreeSelectedImagePath,
            state.CurrentImageFullPath,
            state.CurrentFolderPath,
            state.TreeSelectedFolderPath);
        if (string.IsNullOrEmpty(browsed))
            browsed = state.CurrentFolderPath;

        if (!IsSameOrDescendantDirectory(state.CurrentFolderPath, browsed))
            browsed = state.CurrentFolderPath;

        return browsed;
    }

    private static string? NormalizePathOrSelf(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    /// <returns><see langword="true"/> if <paramref name="candidateFolder"/> is the same path as or nested under <paramref name="ancestorOrSelfFolder"/>.</returns>
    private static bool IsSameOrDescendantDirectory(string ancestorOrSelfFolder, string candidateFolder)
    {
        if (string.IsNullOrEmpty(ancestorOrSelfFolder) || string.IsNullOrEmpty(candidateFolder))
            return false;
        string root;
        string cand;
        try
        {
            root = Path.GetFullPath(ancestorOrSelfFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            cand = Path.GetFullPath(candidateFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return false;
        }

        if (string.Equals(root, cand, StringComparison.OrdinalIgnoreCase))
            return true;

        return cand.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || cand.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><paramref name="path"/> is the browse root itself or a strict descendant file/folder path.</summary>
    private static bool IsUnderBrowseRoot(string browseRootFullPath, string path)
    {
        if (string.IsNullOrEmpty(browseRootFullPath) || string.IsNullOrEmpty(path))
            return false;
        if (string.Equals(browseRootFullPath, path, StringComparison.OrdinalIgnoreCase))
            return true;
        return BrowserTreeDeletePathDedupe.IsStrictDescendantPath(browseRootFullPath, path);
    }
}
