using ImageHoard.Core.Browse;

namespace ImageHoard.App.BrowserV2;

/// <summary>
/// Runtime mirror of <c>paths.browserTree</c> for Browse2; callers sync into <see cref="AppSessionSettings.BrowserTree"/> before <see cref="AppSettingsStore.SaveAll"/>.
/// </summary>
internal sealed class BrowserTreeStore
{
    public BrowserTreeStore(string snapshotBrowseRoot)
    {
        SnapshotBrowseRoot = FavoriteIndexRoots.NormalizeFavoritePath(snapshotBrowseRoot);
    }

    public string SnapshotBrowseRoot { get; }

    public List<string> ExpandedFolderPaths { get; } = new();

    public string? SelectedFolderPath { get; set; }

    public ViewportAnchorDto? ViewportAnchor { get; set; }

    public static BrowserTreeStore? TryFromSession(AppSessionSettings session, string browseRoot)
    {
        if (string.IsNullOrWhiteSpace(browseRoot))
            return null;
        var norm = FavoriteIndexRoots.NormalizeFavoritePath(browseRoot);
        var store = new BrowserTreeStore(norm);
        if (session.BrowserTree is { } bt
            && BrowserTreeSnapshot.IsRestoreRootMatching(bt.SnapshotBrowseRoot, norm))
        {
            store.ExpandedFolderPaths.AddRange(bt.ExpandedFolderPaths);
            store.SelectedFolderPath = string.IsNullOrWhiteSpace(bt.SelectedFolderPath)
                ? null
                : FavoriteIndexRoots.NormalizeFavoritePath(bt.SelectedFolderPath);
            if (bt.ViewportAnchor is { AnchorFolderPath: { Length: > 0 } ap } va)
            {
                store.ViewportAnchor = new ViewportAnchorDto
                {
                    AnchorFolderPath = FavoriteIndexRoots.NormalizeFavoritePath(ap),
                    OffsetWithinRowPx = va.OffsetWithinRowPx,
                };
            }
        }

        return store;
    }

    public void WriteIntoSession(AppSessionSettings session)
    {
        session.BrowserTree = new BrowserTreeSessionSnapshot
        {
            SnapshotBrowseRoot = SnapshotBrowseRoot,
            ExpandedFolderPaths = ExpandedFolderPaths.Count > 0 ? new List<string>(ExpandedFolderPaths) : new List<string>(),
            SelectedFolderPath = string.IsNullOrWhiteSpace(SelectedFolderPath) ? null : SelectedFolderPath,
            ViewportAnchor = ViewportAnchor is { AnchorFolderPath: { Length: > 0 } ap } v
                ? new ViewportAnchorDto
                {
                    AnchorFolderPath = ap,
                    OffsetWithinRowPx = v.OffsetWithinRowPx,
                }
                : null,
        };
    }
}
