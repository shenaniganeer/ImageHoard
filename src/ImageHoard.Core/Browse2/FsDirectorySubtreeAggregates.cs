using ImageHoard.Core.Browse;
using ImageHoard.Core.Models;
using ImageHoard.Core.Services;

namespace ImageHoard.Core.Browse2;

/// <summary>DFS aggregate totals for one directory subtree (shared by background scan and targeted aggregate refresh).</summary>
public static class FsDirectorySubtreeAggregates
{
    public static async Task<(long Agg, int Tot, int Img)> ComputeAsync(
        IFileSystem fileSystem,
        string directoryPath,
        CancellationToken cancellationToken)
    {
        var norm = FavoriteIndexRoots.NormalizeFavoritePath(directoryPath);
        if (!await fileSystem.DirectoryExistsAsync(norm, cancellationToken).ConfigureAwait(false))
            return (0, 0, 0);

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
            var sub = await ComputeAsync(fileSystem, d.FullPath, cancellationToken).ConfigureAwait(false);
            agg += sub.Agg;
            tot += sub.Tot;
            img += sub.Img;
        }

        return (agg, tot, img);
    }
}
