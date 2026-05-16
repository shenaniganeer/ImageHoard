namespace ImageHoard.Core.Browse2;

/// <summary>One visible line in the folders-only tree projection.</summary>
public sealed class FolderRow
{
    public required string IndexRoot { get; init; }

    public required string Path { get; init; }

    public int Depth { get; init; }

    public bool IsExpanded { get; init; }

    public bool HasChildren { get; init; }

    public required string Name { get; init; }

    public long AggregateSizeBytes { get; init; }

    public int TotalFileCount { get; init; }

    public int ImageFileCount { get; init; }

    /// <summary>Formatted subtree size for the folder column.</summary>
    public required string SizeDisplay { get; init; }

    /// <summary>Formatted recursive image file count.</summary>
    public required string ImageCountDisplay { get; init; }

    /// <summary>Formatted directory mtime from the map row.</summary>
    public required string ModifiedDisplay { get; init; }

    public FolderRow WithExpanded(bool isExpanded) =>
        new()
        {
            IndexRoot = IndexRoot,
            Path = Path,
            Depth = Depth,
            IsExpanded = isExpanded,
            HasChildren = HasChildren,
            Name = Name,
            AggregateSizeBytes = AggregateSizeBytes,
            TotalFileCount = TotalFileCount,
            ImageFileCount = ImageFileCount,
            SizeDisplay = SizeDisplay,
            ImageCountDisplay = ImageCountDisplay,
            ModifiedDisplay = ModifiedDisplay,
        };
}
