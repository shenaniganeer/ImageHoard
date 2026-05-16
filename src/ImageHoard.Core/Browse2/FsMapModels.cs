namespace ImageHoard.Core.Browse2;

/// <summary>On-disk Browse2 filesystem map for one deduped favorite index root (format v1).</summary>
public sealed class FsMapDocument
{
    public int FormatVersion { get; set; } = 1;

    public string IndexRoot { get; set; } = "";

    public DateTimeOffset? SavedAtUtc { get; set; }

    /// <summary>Normalized directory path → row (ordinal-ignore-case keys).</summary>
    public Dictionary<string, FsMapEntry> Entries { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// One directory row: structure, mtime trust inputs, subtree aggregates (FR-BR-06/07 aligned),
/// and <see cref="LastVerifiedAtUtc"/> for targeted invalidation.
/// </summary>
public sealed class FsMapEntry
{
    public string ParentPath { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Directory mtime from the last listing that updated this row.</summary>
    public DateTimeOffset? DirectoryMtimeUtc { get; set; }

    /// <summary>True when this directory has at least one immediate child directory (tree chevron).</summary>
    public bool HasSubfolders { get; set; }

    public long AggregateSizeBytes { get; set; }

    public int TotalFileCount { get; set; }

    public int ImageFileCount { get; set; }

    /// <summary>When this row's subtree aggregates were last computed from disk (scanner / full refresh).</summary>
    public DateTimeOffset? LastVerifiedAtUtc { get; set; }

    public bool IsMtimeTrusted(DateTimeOffset? onDiskMtimeUtc) =>
        Metrics.FolderMetricsTrust.FolderMtimeMatches(DirectoryMtimeUtc, onDiskMtimeUtc);
}
