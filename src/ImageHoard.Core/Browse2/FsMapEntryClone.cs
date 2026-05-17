namespace ImageHoard.Core.Browse2;

internal static class FsMapEntryClone
{
    public static FsMapEntry Snapshot(FsMapEntry e) =>
        new()
        {
            ParentPath = e.ParentPath,
            Name = e.Name,
            DirectoryMtimeUtc = e.DirectoryMtimeUtc,
            HasSubfolders = e.HasSubfolders,
            AggregateSizeBytes = e.AggregateSizeBytes,
            TotalFileCount = e.TotalFileCount,
            ImageFileCount = e.ImageFileCount,
            LastVerifiedAtUtc = e.LastVerifiedAtUtc,
        };
}
