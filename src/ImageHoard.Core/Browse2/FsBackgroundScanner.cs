using ImageHoard.Core.Browse;
using ImageHoard.Core.Models;
using ImageHoard.Core.Services;

namespace ImageHoard.Core.Browse2;

/// <summary>
/// One full DFS pass per favorite-backed (persistent) workspace: throttled, cancellable, once started per instance.
/// Transient browse-only workspaces are skipped (they rebuild via targeted refresh on navigation).
/// </summary>
public sealed class FsBackgroundScanner
{
    private int _started;

    public Task? RunningTask { get; private set; }

    public bool HasStarted => Volatile.Read(ref _started) != 0;

    public void StartOnce(
        FsMapRegistry registry,
        IFileSystem fileSystem,
        CancellationToken appCancellationToken,
        FsBackgroundScannerOptions? options = null)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        var opt = options ?? new FsBackgroundScannerOptions();
        RunningTask = Task.Run(
            () => RunAllRootsAsync(registry, fileSystem, opt, appCancellationToken),
            appCancellationToken);
    }

    public async Task RunAllRootsAsync(
        FsMapRegistry registry,
        IFileSystem fileSystem,
        FsBackgroundScannerOptions options,
        CancellationToken cancellationToken)
    {
        // Only favorite-backed FsMaps are persisted; transient workspaces are not scanned here.
        foreach (var ws in registry.AllWorkspaces())
        {
            if (!ws.IsPersistent)
                continue;
            cancellationToken.ThrowIfCancellationRequested();
            var state = new ScanState();
            await ScanDirectoryRecursiveAsync(ws, fileSystem, ws.IndexRoot, options, state, cancellationToken)
                .ConfigureAwait(false);
            await ws.SaveAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class ScanState
    {
        public int DirectoryCounter;
    }

    private static async Task<(long Agg, int Tot, int Img)> ScanDirectoryRecursiveAsync(
        FsMapWorkspace workspace,
        IFileSystem fileSystem,
        string directoryPath,
        FsBackgroundScannerOptions options,
        ScanState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var norm = FavoriteIndexRoots.NormalizeFavoritePath(directoryPath);
        if (!await fileSystem.DirectoryExistsAsync(norm, cancellationToken).ConfigureAwait(false))
            return (0, 0, 0);

        await MaybeYieldAsync(state, options, cancellationToken).ConfigureAwait(false);
        options.DirectoryVisited?.Invoke(norm);

        IReadOnlyList<FileSystemEntry> list;
        try
        {
            list = await fileSystem.ListDirectoryAsync(norm, cancellationToken).ConfigureAwait(false);
        }
        catch (DirectoryNotFoundException)
        {
            return (0, 0, 0);
        }

        var dirs = new List<FileSystemEntry>();
        long agg = 0;
        var tot = 0;
        var img = 0;
        foreach (var e in list)
        {
            if (e.IsDirectory)
            {
                dirs.Add(e);
                continue;
            }

            tot++;
            agg += e.LengthBytes ?? 0;
            if (ImageExtensions.IsImageFile(e.FullPath))
                img++;
        }

        foreach (var d in dirs)
        {
            var sub = await ScanDirectoryRecursiveAsync(
                    workspace,
                    fileSystem,
                    d.FullPath,
                    options,
                    state,
                    cancellationToken)
                .ConfigureAwait(false);
            agg += sub.Agg;
            tot += sub.Tot;
            img += sub.Img;
        }

        var hasSubfolders = dirs.Count > 0;
        var selfMtime = FsDirectoryMetadata.TryGetLastWriteTimeUtc(norm);
        var parent = FsMapPathHelpers.ParentPathOrEmpty(norm, workspace.IndexRoot);
        var name = FsMapPathHelpers.DisplayName(norm, workspace.IndexRoot);
        var verified = DateTimeOffset.UtcNow;
        workspace.UpsertDirectoryRow(
            norm,
            parent,
            name,
            selfMtime,
            hasSubfolders,
            agg,
            tot,
            img,
            verified);

        return (agg, tot, img);
    }

    private static async Task MaybeYieldAsync(
        ScanState state,
        FsBackgroundScannerOptions options,
        CancellationToken cancellationToken)
    {
        if (options.YieldEveryNDirectories <= 0)
            return;
        state.DirectoryCounter++;
        if (state.DirectoryCounter % options.YieldEveryNDirectories != 0)
            return;
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
    }
}
