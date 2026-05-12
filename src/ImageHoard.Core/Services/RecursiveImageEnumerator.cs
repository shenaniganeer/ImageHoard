using System.Runtime.CompilerServices;
using ImageHoard.Core.Browse;
using ImageHoard.Core.Models;

namespace ImageHoard.Core.Services;

/// <summary>
/// Depth-first image enumeration with symlink depth cap and cycle guard (symlink-junction-policy.md).
/// </summary>
public static class RecursiveImageEnumerator
{
    /// <summary>
    /// Enumerates image files under <paramref name="rootDirectory"/> depth-first, dirs first then files (name order per listing).
    /// </summary>
    public static async IAsyncEnumerable<string> EnumerateAsync(
        IFileSystem fileSystem,
        string rootDirectory,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var rootFull = NormalizeVisitedKey(rootDirectory);
        var visitedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var path in EnumerateCoreAsync(
                           fileSystem,
                           rootDirectory,
                           rootFull,
                           symlinkDepthFromRoot: 0,
                           visitedDirs,
                           cancellationToken).ConfigureAwait(false))
        {
            yield return path;
        }
    }

    private static async IAsyncEnumerable<string> EnumerateCoreAsync(
        IFileSystem fileSystem,
        string directoryPath,
        string directoryVisitedKey,
        int symlinkDepthFromRoot,
        HashSet<string> visitedDirs,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!visitedDirs.Add(directoryVisitedKey))
            yield break;

        var entries = await fileSystem.ListDirectoryAsync(directoryPath, cancellationToken).ConfigureAwait(false);

        foreach (var entry in entries.Where(e => e.IsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nextDepth = symlinkDepthFromRoot + (entry.IsReparsePoint ? 1 : 0);
            if (entry.IsReparsePoint && nextDepth > SymlinkTraversalPolicy.MaxSymlinkDepthFromRoot)
                continue;

            var childKey = NormalizeVisitedKey(entry.FullPath);
            await foreach (var img in EnumerateCoreAsync(
                               fileSystem,
                               entry.FullPath,
                               childKey,
                               nextDepth,
                               visitedDirs,
                               cancellationToken).ConfigureAwait(false))
            {
                yield return img;
            }
        }

        foreach (var entry in entries.Where(e => !e.IsDirectory && ImageExtensions.IsImageFile(e.FullPath)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry.FullPath;
        }
    }

    private static string NormalizeVisitedKey(string path)
    {
        try
        {
            return Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch
        {
            return path;
        }
    }
}
