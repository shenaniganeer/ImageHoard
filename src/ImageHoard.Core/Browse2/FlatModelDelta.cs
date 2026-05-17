namespace ImageHoard.Core.Browse2;

/// <summary>One UI-applicable change to the flat folder list (indices are relative to the list after prior changes in the same delta).</summary>
public abstract record FlatModelChange;

public sealed record FlatModelRemoveRange(int StartIndex, int Count) : FlatModelChange;

public sealed record FlatModelInsertRange(int StartIndex, IReadOnlyList<FolderRow> Rows) : FlatModelChange;

public sealed record FlatModelReplaceRow(int Index, FolderRow Row) : FlatModelChange;

/// <summary>Replaces the entire projection (cold boot or hard reset).</summary>
public sealed record FlatModelReset(IReadOnlyList<FolderRow> Rows) : FlatModelChange;

/// <summary>Ordered patch for one logical model tick.</summary>
public sealed class FlatModelDelta
{
    public FlatModelDelta(IReadOnlyList<FlatModelChange> changes) => Changes = changes;

    public IReadOnlyList<FlatModelChange> Changes { get; }

    public bool IsEmpty => Changes.Count == 0;

    public static FlatModelDelta Empty { get; } = new(Array.Empty<FlatModelChange>());
}
