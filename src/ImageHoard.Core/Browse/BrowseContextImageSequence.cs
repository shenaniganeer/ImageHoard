using ImageHoard.Core.Models;
using ImageHoard.Core.Sort;

namespace ImageHoard.Core.Browse;

/// <summary>Sort mode for immediate image files in a folder (mirrors app <c>ListSortKind</c> for browse sequencing).</summary>
public enum BrowseImageListSortKind
{
    NameNatural,
    Name,
    DateModified,
    Size,
}

/// <summary>
/// Builds ordered, browse-mode-filtered image paths for a single directory from directory listings,
/// without relying on UI tree materialization.
/// </summary>
public static class BrowseContextImageSequence
{
    /// <summary>Returns true when <paramref name="contextDirectoryPath"/> is the browse root or a subdirectory of it.</summary>
    public static bool IsContextDirectoryUnderBrowseRoot(string browseRootFolderPath, string contextDirectoryPath)
    {
        if (string.IsNullOrEmpty(browseRootFolderPath) || string.IsNullOrEmpty(contextDirectoryPath))
            return false;

        string root;
        string cand;
        try
        {
            root = Path.GetFullPath(browseRootFolderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            cand = Path.GetFullPath(contextDirectoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return false;
        }

        if (string.Equals(root, cand, StringComparison.OrdinalIgnoreCase))
            return true;

        return cand.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || cand.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static List<FileSystemEntry> PickImmediateImageFiles(IReadOnlyList<FileSystemEntry> directoryListing)
    {
        var list = new List<FileSystemEntry>();
        foreach (var e in directoryListing)
        {
            if (e.IsDirectory)
                continue;
            if (!ImageExtensions.IsImageFile(e.FullPath))
                continue;
            list.Add(e);
        }

        return list;
    }

    public static List<FileSystemEntry> OrderImageFileEntries(
        IReadOnlyList<FileSystemEntry> imageFiles,
        BrowseImageListSortKind sortKind)
    {
        IEnumerable<FileSystemEntry> ordered = sortKind switch
        {
            BrowseImageListSortKind.NameNatural => imageFiles.OrderBy(e => e.Name, NaturalStringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.FullPath, StringComparer.OrdinalIgnoreCase),
            BrowseImageListSortKind.Name => imageFiles.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.FullPath, StringComparer.OrdinalIgnoreCase),
            BrowseImageListSortKind.DateModified => imageFiles
                .OrderByDescending(e => e.LastWriteTimeUtc ?? DateTimeOffset.MinValue)
                .ThenBy(e => e.FullPath, StringComparer.OrdinalIgnoreCase),
            BrowseImageListSortKind.Size => imageFiles.OrderByDescending(e => e.LengthBytes ?? 0)
                .ThenBy(e => e.FullPath, StringComparer.OrdinalIgnoreCase),
            _ => imageFiles.OrderBy(e => e.Name, NaturalStringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.FullPath, StringComparer.OrdinalIgnoreCase),
        };

        return ordered.ToList();
    }

    public static List<string> FilterPathsByNavigationMode(
        IReadOnlyList<FileSystemEntry> orderedImageFiles,
        BrowseNavigationMode mode,
        Func<string, SortFlagState> getState)
    {
        if (mode == BrowseNavigationMode.AllImages)
        {
            var all = new List<string>(orderedImageFiles.Count);
            foreach (var e in orderedImageFiles)
                all.Add(e.FullPath);
            return all;
        }

        var r = new List<string>();
        foreach (var e in orderedImageFiles)
        {
            if (BrowseNavigationModeFilter.Matches(getState(e.FullPath), mode))
                r.Add(e.FullPath);
        }

        return r;
    }
}
