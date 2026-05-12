using System.Linq;
using ImageHoard.Core.Models;
using ImageHoard.Core.Services;

namespace ImageHoard.Core.Browse;

/// <summary>
/// Collects image file paths under a folder for batch operations (recursive vs immediate-only).
/// </summary>
public static class FolderImagePathCollection
{
    public static async Task<List<string>> CollectAsync(
        IFileSystem fileSystem,
        string folderPath,
        bool includeSubfolders,
        CancellationToken cancellationToken = default)
    {
        if (includeSubfolders)
        {
            var list = new List<string>();
            await foreach (var p in RecursiveImageEnumerator.EnumerateAsync(fileSystem, folderPath, cancellationToken)
                               .ConfigureAwait(false))
            {
                list.Add(p);
            }

            return list;
        }

        var entries = await fileSystem.ListDirectoryAsync(folderPath, cancellationToken).ConfigureAwait(false);
        return entries
            .Where(e => !e.IsDirectory && ImageExtensions.IsImageFile(e.FullPath))
            .Select(e => e.FullPath)
            .ToList();
    }
}
