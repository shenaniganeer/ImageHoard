using ImageHoard.Core.Models;
using ImageHoard.Core.Sort;

namespace ImageHoard.Core.Browse;

/// <summary>Stable ordering for directory entries under one parent folder.</summary>
public static class FolderDirectorySort
{
    public static List<FileSystemEntry> SortDirectories(
        IEnumerable<FileSystemEntry> directories,
        FolderListSortKind kind,
        IReadOnlyDictionary<string, long?>? aggregateBytesByPath)
    {
        var list = directories.Where(d => d.IsDirectory).ToList();
        aggregateBytesByPath ??= EmptyAgg.Value;

        switch (kind)
        {
            case FolderListSortKind.DateModified:
                list.Sort(CompareDateModified);
                break;
            case FolderListSortKind.AggregateSize:
                list.Sort((a, b) => CompareAggregateSize(a, b, aggregateBytesByPath));
                break;
            default:
                list.Sort(CompareNameNatural);
                break;
        }

        return list;
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

    private static long? TryGetAgg(IReadOnlyDictionary<string, long?> map, string path)
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
}
