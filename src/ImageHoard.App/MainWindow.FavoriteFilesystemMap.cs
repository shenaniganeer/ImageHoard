using System.Collections.Concurrent;
using ImageHoard.Core.Browse;
using ImageHoard.Core.Metrics;

namespace ImageHoard.App;

public sealed partial class MainWindow
{
    private readonly ConcurrentQueue<string> _favoriteMapBackgroundIndexRootQueue = new();
    private bool _favoriteMapBackgroundDrainUiPending;

    private async Task SeedFavoriteFilesystemMapsIntoAggregateCachesAsync(int populateGen)
    {
        var roots = FavoriteIndexRoots.ComputeMinimalIndexRoots(_session.Favorites);
        foreach (var r in roots)
        {
            if (populateGen != Volatile.Read(ref _populateBrowserGeneration))
                return;
            var fp = FavoriteFilesystemMapStore.MapFilePathForIndexRoot(AppDataPaths.FavoriteFilesystemMapsDirectory, r);
            FavoriteFilesystemMapDocument? doc;
            try
            {
                doc = await FavoriteFilesystemMapStore.TryLoadAsync(fp).ConfigureAwait(false);
            }
            catch
            {
                continue;
            }

            if (doc?.Entries == null)
                continue;
            foreach (var kv in doc.Entries)
            {
                if (populateGen != Volatile.Read(ref _populateBrowserGeneration))
                    return;
                _folderAggregateBytesByPath[kv.Key] = kv.Value.AggregateSizeBytes;
                _folderImageFileCountByPath[kv.Key] = kv.Value.ImageFileCount;
            }
        }
    }

    private void KickFavoriteFilesystemMapBackgroundReconcileForIndexRoots()
    {
        if (!_layoutState.ShowBrowserFolderSize && !_layoutState.ShowBrowserFolderImageCount)
        {
            return;
        }

        foreach (var r in FavoriteIndexRoots.ComputeMinimalIndexRoots(_session.Favorites))
            _favoriteMapBackgroundIndexRootQueue.Enqueue(r);
        ScheduleFavoriteMapBackgroundDrain();
    }

    private void ScheduleFavoriteMapBackgroundDrain()
    {
        if (_favoriteMapBackgroundDrainUiPending)
            return;
        _favoriteMapBackgroundDrainUiPending = true;
        _ = DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            ProcessFavoriteMapBackgroundDrainTick);
    }

    private void ProcessFavoriteMapBackgroundDrainTick()
    {
        _favoriteMapBackgroundDrainUiPending = false;
        for (var i = 0; i < 2; i++)
        {
            if (!_favoriteMapBackgroundIndexRootQueue.TryDequeue(out var root))
                break;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                continue;
            _ = StartFolderMetricsWorkAsync(root, FolderMetricsScanScope.FullSubtree, ignoreCache: false);
        }

        if (!_favoriteMapBackgroundIndexRootQueue.IsEmpty)
            ScheduleFavoriteMapBackgroundDrain();
    }

    private Task TryPersistFavoriteFilesystemMapSnapshotAsync(FolderMetricsSnapshot snap)
    {
        if (snap.ScanScope != FolderMetricsScanScope.FullSubtree)
            return Task.CompletedTask;
        var roots = FavoriteIndexRoots.ComputeMinimalIndexRoots(_session.Favorites);
        var owning = FavoriteIndexRoots.FindOwningIndexRoot(snap.Path, roots);
        if (owning == null)
            return Task.CompletedTask;
        return FavoriteFilesystemMapStore.TryUpsertSubtreeSnapshotAsync(
            AppDataPaths.FavoriteFilesystemMapsDirectory,
            owning,
            snap,
            CancellationToken.None);
    }

    private async Task PurgeFavoriteFilesystemMapsForPrefixesAsync(IEnumerable<string> purgeFolderPrefixes)
    {
        try
        {
            var roots = FavoriteIndexRoots.ComputeMinimalIndexRoots(_session.Favorites);
            await FavoriteFilesystemMapStore
                .PurgePathsAsync(
                    AppDataPaths.FavoriteFilesystemMapsDirectory,
                    roots,
                    purgeFolderPrefixes,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            // ignored
        }
    }

    private static HashSet<string> CollectAncestorDirectoryPrefixesForFavoriteMapPurge(string fileOrFolderPath)
    {
        var purge = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(fileOrFolderPath))
            return purge;
        var d = Path.GetDirectoryName(fileOrFolderPath);
        while (!string.IsNullOrEmpty(d))
        {
            purge.Add(d);
            d = Directory.GetParent(d)?.FullName;
        }

        return purge;
    }
}
