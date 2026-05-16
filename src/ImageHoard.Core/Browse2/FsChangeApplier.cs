using ImageHoard.Core.Browse;
using ImageHoard.Core.Services;

namespace ImageHoard.Core.Browse2;

/// <summary>Patches maps after successful app-driven IO (rename/move/recycle).</summary>
public sealed class FsChangeApplier
{
    private readonly FsTargetedRefresher _refresher;

    public FsChangeApplier(IFileSystem fileSystem, FsMapRegistry registry)
    {
        _refresher = new FsTargetedRefresher(fileSystem, registry);
    }

    public FsChangeApplier(FsTargetedRefresher targetedRefresher)
    {
        _refresher = targetedRefresher;
    }

    public async Task ApplyRecycleAsync(FsMapRegistry registry, string deletedPath, CancellationToken cancellationToken = default)
    {
        var norm = FavoriteIndexRoots.NormalizeFavoritePath(deletedPath);
        var ws = registry.TryGetWorkspaceForPath(norm);
        if (ws == null)
            return;
        ws.RemoveSubtree(norm);
        await ws.SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>After image file(s) were removed from disk, patch per-folder subtree aggregates for affected ancestor rows.</summary>
    public async Task ApplyWizardRemovedImageFilesAsync(
        FsMapRegistry registry,
        IReadOnlyList<(string FullPath, long LengthBytes, bool IsImage)> succeeded,
        CancellationToken cancellationToken = default)
    {
        if (succeeded.Count == 0)
            return;

        var byRoot = new Dictionary<string, List<(string FullPath, long LengthBytes, bool IsImage)>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var s in succeeded)
        {
            var ws = registry.TryGetWorkspaceForPath(s.FullPath);
            if (ws == null)
                continue;
            if (!byRoot.TryGetValue(ws.IndexRoot, out var list))
            {
                list = new List<(string FullPath, long LengthBytes, bool IsImage)>();
                byRoot[ws.IndexRoot] = list;
            }

            list.Add(s);
        }

        foreach (var kv in byRoot)
        {
            var ws = registry.TryGetWorkspace(kv.Key);
            if (ws == null)
                continue;
            ws.ApplyWizardRemovedImageFileStats(kv.Value);
            await ws.SaveAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Directory rename or move. Same index-root uses in-map remap; cross-root removes source and refreshes destination parents.</summary>
    public async Task ApplyDirectoryMoveAsync(
        FsMapRegistry registry,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var src = FavoriteIndexRoots.NormalizeFavoritePath(sourcePath);
        var dst = FavoriteIndexRoots.NormalizeFavoritePath(destinationPath);
        if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase))
            return;

        var srcRoot = FavoriteIndexRoots.FindOwningIndexRoot(src, registry.IndexRoots);
        var dstRoot = FavoriteIndexRoots.FindOwningIndexRoot(dst, registry.IndexRoots);
        var srcWs = srcRoot == null ? null : registry.TryGetWorkspace(srcRoot);
        var dstWs = dstRoot == null ? null : registry.TryGetWorkspace(dstRoot);

        if (srcWs != null
            && dstWs != null
            && string.Equals(srcWs.IndexRoot, dstWs.IndexRoot, StringComparison.OrdinalIgnoreCase))
        {
            srcWs.RemapSubtreePrefix(src, dst);
            await srcWs.SaveAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (srcWs != null)
        {
            srcWs.RemoveSubtree(src);
            await srcWs.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        if (dstWs == null)
            return;

        var parent = FsMapPathHelpers.ParentPathOrEmpty(dst, dstWs.IndexRoot);
        if (!string.IsNullOrEmpty(parent))
            await _refresher.RefreshAsync(parent, cancellationToken).ConfigureAwait(false);
        await _refresher.RefreshAsync(dst, cancellationToken).ConfigureAwait(false);
    }

    public Task ApplyDirectoryRenameAsync(
        FsMapRegistry registry,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        ApplyDirectoryMoveAsync(registry, sourcePath, destinationPath, cancellationToken);
}
