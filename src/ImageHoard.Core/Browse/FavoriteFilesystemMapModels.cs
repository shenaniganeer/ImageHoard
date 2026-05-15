using ImageHoard.Core.Metrics;

namespace ImageHoard.Core.Browse;

/// <summary>On-disk favorite subtree map (format v1).</summary>
public sealed class FavoriteFilesystemMapDocument
{
    public int FormatVersion { get; set; } = 1;

    public string IndexRoot { get; set; } = "";

    public DateTimeOffset? SavedAtUtc { get; set; }

    /// <summary>Directory path key → cached subtree metrics (FR-BR-06 fields).</summary>
    public Dictionary<string, FavoriteFilesystemMapEntry> Entries { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FavoriteFilesystemMapEntry
{
    public long AggregateSizeBytes { get; set; }

    public int TotalFileCount { get; set; }

    public int ImageFileCount { get; set; }

    public DateTimeOffset? FolderMtimeUtc { get; set; }

    public FolderMetricsSnapshot ToSnapshot(string path) =>
        new(
            path,
            AggregateSizeBytes,
            TotalFileCount,
            ImageFileCount,
            FolderMtimeUtc,
            FolderMetricsScanScope.FullSubtree,
            HasExpandableChildren: null);
}
