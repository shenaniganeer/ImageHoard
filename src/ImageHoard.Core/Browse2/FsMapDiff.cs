namespace ImageHoard.Core.Browse2;

/// <summary>Strongly-typed map mutation for subscribers (tree + image pane).</summary>
public abstract record FsMapDiff(string IndexRoot);

public sealed record FsFolderAddedDiff(string IndexRoot, string Path, string ParentPath, FsMapEntry Snapshot)
    : FsMapDiff(IndexRoot);

public sealed record FsFolderRemovedDiff(string IndexRoot, string Path, string ParentPath) : FsMapDiff(IndexRoot);

public sealed record FsFolderRenamedDiff(
    string IndexRoot,
    string OldPath,
    string NewPath,
    string OldParentPath,
    string NewParentPath) : FsMapDiff(IndexRoot);

/// <summary>Folder row replaced in-place (listing, mtime, shallow flags, or aggregates).</summary>
public sealed record FsFolderRefreshedDiff(string IndexRoot, string Path, FsMapEntry? Before, FsMapEntry After)
    : FsMapDiff(IndexRoot);

public sealed record FsAggregatesUpdatedDiff(
    string IndexRoot,
    string Path,
    long AggregateSizeBytes,
    int TotalFileCount,
    int ImageFileCount) : FsMapDiff(IndexRoot);
