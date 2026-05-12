using System.Runtime.CompilerServices;
using ImageHoard.Core.Browse;
using ImageHoard.Core.Models;
using ImageHoard.Core.Services;

namespace ImageHoard.Core.Metrics;

public sealed record FolderMetricsSnapshot(
    string Path,
    long AggregateSizeBytes,
    int TotalFileCount,
    int ImageFileCount,
    DateTimeOffset? FolderMtimeUtc);

/// <summary>FR-BR-06 — single-folder subtree scan (full subtree).</summary>
public static class FolderMetricsScanner
{
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

        return new FolderMetricsSnapshot(directoryPath, size, files, images, dirMtime);
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
