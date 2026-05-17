using System.Linq;
using System.Runtime.CompilerServices;
using ImageHoard.Core.Browse;
using ImageHoard.Core.Models;

namespace ImageHoard.Core.Services;

/// <summary>
/// Depth-first image enumeration with symlink depth cap and cycle guard (symlink-junction-policy.md).
/// Default child order follows <see cref="IFileSystem.ListDirectoryAsync"/> (deterministic for browse).
/// Optional <see cref="Random"/> shuffles immediate child directories (and image files in each folder) per node for slideshow discovery.
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
        await foreach (var path in EnumerateAsync(fileSystem, rootDirectory, shuffleChildren: null, cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return path;
        }
    }

    /// <summary>
    /// Enumerates image files under <paramref name="rootDirectory"/> depth-first, yielding full <see cref="FileSystemEntry"/>
    /// rows (size + mtime) for browse image-pane sorting.
    /// </summary>
    public static async IAsyncEnumerable<FileSystemEntry> EnumerateImageEntriesAsync(
        IFileSystem fileSystem,
        string rootDirectory,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var rootFull = NormalizeVisitedKey(rootDirectory);
        var visitedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var entry in EnumerateImageEntriesCoreAsync(
                           fileSystem,
                           rootDirectory,
                           rootFull,
                           symlinkDepthFromRoot: 0,
                           visitedDirs,
                           cancellationToken).ConfigureAwait(false))
        {
            yield return entry;
        }
    }

    /// <summary>
    /// Enumerates image files depth-first. When <paramref name="shuffleChildren"/> is non-null, child directory order
    /// and immediate image file order under each directory are shuffled (Fisher–Yates) so streaming discovery is not
    /// locked to alphabetical DFS (tree slideshow randomness).
    /// </summary>
    public static async IAsyncEnumerable<string> EnumerateAsync(
        IFileSystem fileSystem,
        string rootDirectory,
        Random? shuffleChildren,
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
                           shuffleChildren,
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
        Random? shuffleChildren,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!visitedDirs.Add(directoryVisitedKey))
            yield break;

        var entries = await fileSystem.ListDirectoryAsync(directoryPath, cancellationToken).ConfigureAwait(false);

        var dirs = entries.Where(e => e.IsDirectory).ToList();
        var files = entries.Where(e => !e.IsDirectory && ImageExtensions.IsImageFile(e.FullPath)).ToList();
        if (shuffleChildren != null)
        {
            ShuffleInPlace(dirs, shuffleChildren);
            ShuffleInPlace(files, shuffleChildren);
        }

        foreach (var entry in dirs)
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
                               shuffleChildren,
                               cancellationToken).ConfigureAwait(false))
            {
                yield return img;
            }
        }

        foreach (var entry in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry.FullPath;
        }
    }

    private static async IAsyncEnumerable<FileSystemEntry> EnumerateImageEntriesCoreAsync(
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

        var dirs = entries.Where(e => e.IsDirectory).ToList();
        var files = entries.Where(e => !e.IsDirectory && ImageExtensions.IsImageFile(e.FullPath)).ToList();

        foreach (var entry in dirs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nextDepth = symlinkDepthFromRoot + (entry.IsReparsePoint ? 1 : 0);
            if (entry.IsReparsePoint && nextDepth > SymlinkTraversalPolicy.MaxSymlinkDepthFromRoot)
                continue;

            var childKey = NormalizeVisitedKey(entry.FullPath);
            await foreach (var img in EnumerateImageEntriesCoreAsync(
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

        foreach (var entry in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
        }
    }

    internal static void ShuffleInPlace<T>(IList<T> list, Random random)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
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
