using ImageHoard.Core.Models;
using ImageHoard.Core.Sort;

namespace ImageHoard.Core.Browse;

/// <summary>Stable ordering for directory entries under one parent folder.</summary>
public static class FolderDirectorySort
{
    public static List<FileSystemEntry> SortDirectories(
        IEnumerable<FileSystemEntry> directories,
        FolderListSortKind kind,
        IReadOnlyDictionary<string, long?>? aggregateBytesByPath,
        IReadOnlyDictionary<string, int?>? imageFileCountByPath = null)
    {
        var list = directories.Where(d => d.IsDirectory).ToList();
        aggregateBytesByPath ??= EmptyAgg.Value;
        imageFileCountByPath ??= EmptyImg.Value;

        switch (kind)
        {
            case FolderListSortKind.DateModified:
                list.Sort(CompareDateModified);
                break;
            case FolderListSortKind.AggregateSize:
                list.Sort((a, b) => CompareAggregateSize(a, b, aggregateBytesByPath));
                break;
            case FolderListSortKind.ImageFileCount:
                list.Sort((a, b) => CompareImageFileCount(a, b, imageFileCountByPath));
                break;
            default:
                list.Sort(CompareNameNatural);
                break;
        }

        return list;
    }

    /// <summary>
    /// After removing <paramref name="removedFullPath"/> from the parent's directory list, pick a sibling
    /// to refocus: the next directory in <paramref name="sortedDirectories"/> order if any, otherwise the
    /// previous directory, otherwise <see langword="null"/>. The list must already be sorted (e.g. from
    /// <see cref="SortDirectories"/>). Paths are compared case-insensitively.
    /// </summary>
    public static FileSystemEntry? PickAdjacentSiblingAfterRemoval(
        IReadOnlyList<FileSystemEntry> sortedDirectories,
        string removedFullPath)
    {
        if (sortedDirectories.Count == 0 || string.IsNullOrEmpty(removedFullPath))
            return null;

        string removedNorm;
        try
        {
            removedNorm = Path.GetFullPath(removedFullPath);
        }
        catch
        {
            removedNorm = removedFullPath;
        }

        var idx = -1;
        for (var i = 0; i < sortedDirectories.Count; i++)
        {
            var e = sortedDirectories[i];
            if (!e.IsDirectory)
                continue;
            string p;
            try
            {
                p = Path.GetFullPath(e.FullPath);
            }
            catch
            {
                p = e.FullPath;
            }

            if (string.Equals(p, removedNorm, StringComparison.OrdinalIgnoreCase))
            {
                idx = i;
                break;
            }
        }

        if (idx < 0)
            return null;

        if (idx + 1 < sortedDirectories.Count)
            return sortedDirectories[idx + 1];

        if (idx - 1 >= 0)
            return sortedDirectories[idx - 1];

        return null;
    }

    private static int CompareNameNatural(FileSystemEntry a, FileSystemEntry b)
    {
        var c = NaturalStringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
        if (c != 0)
            return c;
        return string.CompareOrdinal(a.FullPath, b.FullPath);
    }

    private static int CompareDateModified(FileSystemEntry a, FileSystemEntry b)
    {
        var ta = a.LastWriteTimeUtc;
        var tb = b.LastWriteTimeUtc;
        if (ta == null && tb == null)
            return CompareNameNatural(a, b);
        if (ta == null)
            return 1;
        if (tb == null)
            return -1;
        var c = tb.Value.CompareTo(ta.Value);
        if (c != 0)
            return c;
        return string.CompareOrdinal(a.FullPath, b.FullPath);
    }

    private static int CompareAggregateSize(
        FileSystemEntry a,
        FileSystemEntry b,
        IReadOnlyDictionary<string, long?> aggregateBytesByPath)
    {
        var sa = TryGetAgg(aggregateBytesByPath, a.FullPath);
        var sb = TryGetAgg(aggregateBytesByPath, b.FullPath);
        var ha = sa.HasValue ? 0 : 1;
        var hb = sb.HasValue ? 0 : 1;
        var c = ha.CompareTo(hb);
        if (c != 0)
            return c;
        if (ha == 0)
        {
            c = sb!.Value.CompareTo(sa!.Value);
            if (c != 0)
                return c;
        }

        return CompareNameNatural(a, b);
    }

    private static int CompareImageFileCount(
        FileSystemEntry a,
        FileSystemEntry b,
        IReadOnlyDictionary<string, int?> imageFileCountByPath)
    {
        var ia = TryGetImg(imageFileCountByPath, a.FullPath);
        var ib = TryGetImg(imageFileCountByPath, b.FullPath);
        var ha = ia.HasValue ? 0 : 1;
        var hb = ib.HasValue ? 0 : 1;
        var c = ha.CompareTo(hb);
        if (c != 0)
            return c;
        if (ha == 0)
        {
            c = ib!.Value.CompareTo(ia!.Value);
            if (c != 0)
                return c;
        }

        return CompareNameNatural(a, b);
    }

    private static long? TryGetAgg(IReadOnlyDictionary<string, long?> map, string path)
    {
        if (map.TryGetValue(path, out var v))
            return v;
        return null;
    }

    private static int? TryGetImg(IReadOnlyDictionary<string, int?> map, string path)
    {
        if (map.TryGetValue(path, out var v))
            return v;
        return null;
    }

    private static class EmptyAgg
    {
        public static readonly IReadOnlyDictionary<string, long?> Value =
            new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase);
    }

    private static class EmptyImg
    {
        public static readonly IReadOnlyDictionary<string, int?> Value =
            new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
    }
}
