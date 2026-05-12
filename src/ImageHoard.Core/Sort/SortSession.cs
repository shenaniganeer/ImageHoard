namespace ImageHoard.Core.Sort;

/// <summary>Per-folder sort decisions (FR-SR-01).</summary>
public sealed class SortSession
{
    private readonly Dictionary<string, SortFlagState> _byPath = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, SortFlagState> States => _byPath;

    public void SetState(string imagePath, SortFlagState state) =>
        _byPath[imagePath] = state;

    public SortFlagState GetState(string imagePath) =>
        _byPath.TryGetValue(imagePath, out var s) ? s : SortFlagState.Unset;

    public void Clear() => _byPath.Clear();

    public (int Keep, int Delete, int Unset) CountStates(IEnumerable<string> orderedPaths)
    {
        var keep = 0;
        var delete = 0;
        var unset = 0;
        foreach (var p in orderedPaths)
        {
            switch (GetState(p))
            {
                case SortFlagState.Keep:
                    keep++;
                    break;
                case SortFlagState.Delete:
                    delete++;
                    break;
                default:
                    unset++;
                    break;
            }
        }

        return (keep, delete, unset);
    }
}
