namespace ImageHoard.Core.Metrics;

/// <summary>Shared rules for trusting cached folder metrics against on-disk directory mtime (FR-BR-07).</summary>
public static class FolderMetricsTrust
{
    public static bool FolderMtimeMatches(DateTimeOffset? cached, DateTimeOffset? onDisk) =>
        cached.HasValue == onDisk.HasValue && (!cached.HasValue || cached.Value.Equals(onDisk!.Value));

    /// <summary>Whether a cached full-subtree snapshot is safe to use for UI totals without rescanning the tree.</summary>
    public static bool IsTrustedCachedSubtree(
        FolderMetricsSnapshot? snapshot,
        DateTimeOffset? directoryMtimeUtc,
        bool ignoreCache) =>
        !ignoreCache
        && snapshot is { ScanScope: FolderMetricsScanScope.FullSubtree }
        && FolderMtimeMatches(snapshot.FolderMtimeUtc, directoryMtimeUtc);
}
