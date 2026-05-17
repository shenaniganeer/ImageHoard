using ImageHoard.Core.Browse;
using ImageHoard.Core.Models;
using ImageHoard.Core.Services;

namespace ImageHoard.Core.Browse2;

/// <summary>Re-lists one folder, reconciles immediate child folder rows, and invalidates child mtime trust.</summary>
public sealed class FsTargetedRefresher
{
    private readonly IFileSystem _fileSystem;
    private readonly FsMapRegistry _registry;

    public FsTargetedRefresher(IFileSystem fileSystem, FsMapRegistry registry)
    {
        _fileSystem = fileSystem;
        _registry = registry;
    }

    public async Task RefreshAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var norm = FavoriteIndexRoots.NormalizeFavoritePath(folderPath);
        var ws = _registry.TryGetWorkspaceForPath(norm);
        if (ws == null)
            return;

        if (!await _fileSystem.DirectoryExistsAsync(norm, cancellationToken).ConfigureAwait(false))
        {
            ws.RemoveSubtree(norm);
            await ws.SaveAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        ws.InvalidateImmediateChildTrust(norm);

        var listedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<FileSystemEntry> list;
        try
        {
            list = await _fileSystem.ListDirectoryAsync(norm, cancellationToken).ConfigureAwait(false);
        }
        catch (DirectoryNotFoundException)
        {
            ws.RemoveSubtree(norm);
            await ws.SaveAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        foreach (var e in list)
        {
            if (!e.IsDirectory)
                continue;
            listedDirs.Add(FavoriteIndexRoots.NormalizeFavoritePath(e.FullPath));
        }

        var existingChildren = ws.GetDirectChildPaths(norm);
        var existingSet = new HashSet<string>(existingChildren, StringComparer.OrdinalIgnoreCase);

        foreach (var child in listedDirs)
        {
            var mtime = TryChildMtime(list, child);
            if (existingSet.Contains(child))
            {
                ws.PatchDirectoryMtime(child, mtime);
                continue;
            }

            var nm = Path.GetFileName(child);
            if (string.IsNullOrEmpty(nm))
                continue;
            var parent = norm;
            ws.UpsertDirectoryRow(
                child,
                parent,
                nm,
                mtime,
                hasSubfolders: false,
                aggregateSizeBytes: 0,
                totalFileCount: 0,
                imageFileCount: 0,
                lastVerifiedAtUtc: null);
        }

        foreach (var child in existingChildren)
        {
            if (listedDirs.Contains(child))
                continue;
            ws.RemoveSubtree(child);
        }

        var parentMtime = FsDirectoryMetadata.TryGetLastWriteTimeUtc(norm);
        var hasSubfolders = listedDirs.Count > 0;
        var parentName = FsMapPathHelpers.DisplayName(norm, ws.IndexRoot);
        long pAgg = 0;
        var pTot = 0;
        var pImg = 0;
        if (ws.TryGetEntry(norm, out var parentRow))
        {
            pAgg = parentRow.AggregateSizeBytes;
            pTot = parentRow.TotalFileCount;
            pImg = parentRow.ImageFileCount;
        }

        ws.UpsertDirectoryRow(
            norm,
            FsMapPathHelpers.ParentPathOrEmpty(norm, ws.IndexRoot),
            parentName,
            parentMtime,
            hasSubfolders,
            pAgg,
            pTot,
            pImg,
            lastVerifiedAtUtc: DateTimeOffset.UtcNow);

        await ws.SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Recomputes subtree aggregates for each immediate child folder of <paramref name="parentPath"/> that already exists in the map,
    /// updates <see cref="HasSubfolders"/> from a fresh listing, and raises aggregate diffs for tree resort (Browse2 size/image-count sorts).
    /// </summary>
    public async Task RefreshAggregatesForDirectChildrenAsync(string parentPath, CancellationToken cancellationToken = default)
    {
        var norm = FavoriteIndexRoots.NormalizeFavoritePath(parentPath);
        var ws = _registry.TryGetWorkspaceForPath(norm);
        if (ws == null)
            return;

        if (!await _fileSystem.DirectoryExistsAsync(norm, cancellationToken).ConfigureAwait(false))
            return;

        var children = ws.GetDirectChildPaths(norm);
        foreach (var child in children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await _fileSystem.DirectoryExistsAsync(child, cancellationToken).ConfigureAwait(false))
                continue;

            if (ws.TryGetEntry(child, out var existing))
            {
                var onDiskMtime = FsDirectoryMetadata.TryGetLastWriteTimeUtc(child);
                if (existing.LastVerifiedAtUtc != null && existing.IsMtimeTrusted(onDiskMtime))
                    continue;
            }

            IReadOnlyList<FileSystemEntry> list;
            try
            {
                list = await _fileSystem.ListDirectoryAsync(child, cancellationToken).ConfigureAwait(false);
            }
            catch (DirectoryNotFoundException)
            {
                ws.RemoveSubtree(child);
                continue;
            }

            var listedDirs = new List<FileSystemEntry>();
            foreach (var e in list)
            {
                if (e.IsDirectory)
                    listedDirs.Add(e);
            }

            var (agg, tot, img) = await FsDirectorySubtreeAggregates.ComputeAsync(_fileSystem, child, cancellationToken)
                .ConfigureAwait(false);

            var hasSubfolders = listedDirs.Count > 0;
            var selfMtime = FsDirectoryMetadata.TryGetLastWriteTimeUtc(child);
            var nm = Path.GetFileName(child);
            if (string.IsNullOrEmpty(nm))
                continue;

            ws.UpsertDirectoryRow(
                child,
                norm,
                nm,
                selfMtime,
                hasSubfolders,
                agg,
                tot,
                img,
                DateTimeOffset.UtcNow);

            foreach (var d in listedDirs)
            {
                var subPath = FavoriteIndexRoots.NormalizeFavoritePath(d.FullPath);
                if (ws.TryGetEntry(subPath, out _))
                    continue;
                ws.UpsertDirectoryRow(
                    subPath,
                    child,
                    Path.GetFileName(subPath),
                    d.LastWriteTimeUtc,
                    hasSubfolders: false,
                    0,
                    0,
                    0,
                    lastVerifiedAtUtc: null);
            }

            ws.EmitAggregatesUpdatedForPath(child);
        }

        await ws.SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DateTimeOffset? TryChildMtime(IReadOnlyList<FileSystemEntry> parentListing, string childPath)
    {
        foreach (var e in parentListing)
        {
            if (!e.IsDirectory)
                continue;
            if (string.Equals(
                    FavoriteIndexRoots.NormalizeFavoritePath(e.FullPath),
                    childPath,
                    StringComparison.OrdinalIgnoreCase))
                return e.LastWriteTimeUtc;
        }

        return null;
    }
}
