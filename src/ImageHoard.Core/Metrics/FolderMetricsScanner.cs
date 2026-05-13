using System.Runtime.CompilerServices;
using ImageHoard.Core.Browse;
using ImageHoard.Core.Models;
using ImageHoard.Core.Services;

namespace ImageHoard.Core.Metrics;

/// <summary>What a <see cref="FolderMetricsSnapshot"/> counted on disk (FR-BR-06 depth).</summary>
public enum FolderMetricsScanScope
{
    /// <summary>Default for legacy cache rows: entire directory tree under <see cref="FolderMetricsSnapshot.Path"/>.</summary>
    FullSubtree = 0,
    /// <summary>Files directly in the directory only; no subdirectory descent.</summary>
    ImmediateChildren = 1,
}

public sealed record FolderMetricsSnapshot(
    string Path,
    long AggregateSizeBytes,
    int TotalFileCount,
    int ImageFileCount,
    DateTimeOffset? FolderMtimeUtc,
    FolderMetricsScanScope ScanScope = FolderMetricsScanScope.FullSubtree);

/// <summary>FR-BR-06 — folder metrics: immediate listing or full subtree scan.</summary>
public static class FolderMetricsScanner
{
    /// <summary>Counts only non-directory entries in <paramref name="directoryPath"/> (one list call).</summary>
    public static async Task<FolderMetricsSnapshot> ScanImmediateFilesAsync(
        IFileSystem fileSystem,
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        long size = 0;
        var files = 0;
        var images = 0;
        DateTimeOffset? dirMtime = null;

        try
        {
            var di = new DirectoryInfo(directoryPath);
            if (di.Exists)
                dirMtime = new DateTimeOffset(di.LastWriteTimeUtc);
        }
        catch
        {
            // ignored
        }

        IReadOnlyList<FileSystemEntry> entries;
        try
        {
            entries = await fileSystem.ListDirectoryAsync(directoryPath, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return new FolderMetricsSnapshot(directoryPath, 0, 0, 0, dirMtime, FolderMetricsScanScope.ImmediateChildren);
        }

        foreach (var entry in entries.Where(e => !e.IsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            files++;
            try
            {
                long len;
                if (entry.LengthBytes is { } lb)
                    len = lb;
                else
                {
                    var fi = new FileInfo(entry.FullPath);
                    len = fi.Length;
                }

                size += len;
                if (ImageExtensions.IsImageFile(entry.FullPath))
                    images++;
            }
            catch
            {
                // skip unreadable
            }
        }

        return new FolderMetricsSnapshot(directoryPath, size, files, images, dirMtime, FolderMetricsScanScope.ImmediateChildren);
    }

    public static async Task<FolderMetricsSnapshot> ScanSubtreeAsync(
        IFileSystem fileSystem,
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        long size = 0;
        var files = 0;
        var images = 0;
        DateTimeOffset? dirMtime = null;

        try
        {
            var di = new DirectoryInfo(directoryPath);
            if (di.Exists)
                dirMtime = new DateTimeOffset(di.LastWriteTimeUtc);
        }
        catch
        {
            // ignored
        }

        await foreach (var filePath in EnumerateAllFilesRecursiveAsync(fileSystem, directoryPath, cancellationToken)
                           .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            files++;
            try
            {
                var fi = new FileInfo(filePath);
                size += fi.Length;
                if (ImageExtensions.IsImageFile(filePath))
                    images++;
            }
            catch
            {
                // skip unreadable
            }
        }

        return new FolderMetricsSnapshot(directoryPath, size, files, images, dirMtime, FolderMetricsScanScope.FullSubtree);
    }

    private static async IAsyncEnumerable<string> EnumerateAllFilesRecursiveAsync(
        IFileSystem fileSystem,
        string rootDirectory,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var visitedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var p in WalkAsync(fileSystem, rootDirectory, 0, visitedDirs, cancellationToken)
                           .ConfigureAwait(false))
            yield return p;
    }

    private static async IAsyncEnumerable<string> WalkAsync(
        IFileSystem fileSystem,
        string directoryPath,
        int symlinkDepthFromRoot,
        HashSet<string> visitedDirs,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string dirKey;
        try
        {
            dirKey = Path.GetFullPath(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch
        {
            yield break;
        }

        if (!visitedDirs.Add(dirKey))
            yield break;

        IReadOnlyList<FileSystemEntry> entries;
        try
        {
            entries = await fileSystem.ListDirectoryAsync(directoryPath, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            yield break;
        }

        foreach (var entry in entries.Where(e => e.IsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nextDepth = symlinkDepthFromRoot + (entry.IsReparsePoint ? 1 : 0);
            if (entry.IsReparsePoint && nextDepth > SymlinkTraversalPolicy.MaxSymlinkDepthFromRoot)
                continue;

            await foreach (var f in WalkAsync(fileSystem, entry.FullPath, nextDepth, visitedDirs, cancellationToken)
                               .ConfigureAwait(false))
                yield return f;
        }

        foreach (var entry in entries.Where(e => !e.IsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry.FullPath;
        }
    }
}
