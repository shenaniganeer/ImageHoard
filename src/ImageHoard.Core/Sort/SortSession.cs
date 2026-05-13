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

    /// <summary>After a directory rename/move on disk, remap stored flag keys under <paramref name="oldDirectoryPath"/>.</summary>
    public void RelocatePathsForDirectoryRename(string oldDirectoryPath, string newDirectoryPath)
    {
        if (string.IsNullOrEmpty(oldDirectoryPath) || string.IsNullOrEmpty(newDirectoryPath))
            return;

        string oldDir;
        string newDir;
        try
        {
            oldDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(oldDirectoryPath));
            newDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(newDirectoryPath));
        }
        catch
        {
            oldDir = oldDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            newDir = newDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        if (string.Equals(oldDir, newDir, StringComparison.OrdinalIgnoreCase))
            return;

        var sep = Path.DirectorySeparatorChar;
        var keys = _byPath.Keys.ToList();
        foreach (var oldKey in keys)
        {
            if (!_byPath.TryGetValue(oldKey, out var state))
                continue;
            string fileFp;
            try
            {
                fileFp = Path.GetFullPath(oldKey);
            }
            catch
            {
                fileFp = oldKey;
            }

            if (!fileFp.StartsWith(oldDir + sep, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetDirectoryName(fileFp), oldDir, StringComparison.OrdinalIgnoreCase))
                continue;

            string newKey;
            try
            {
                if (string.Equals(Path.GetDirectoryName(fileFp), oldDir, StringComparison.OrdinalIgnoreCase))
                    newKey = Path.Combine(newDir, Path.GetFileName(fileFp));
                else
                    newKey = newDir + fileFp.Substring(oldDir.Length);
            }
            catch
            {
                continue;
            }

            if (string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase))
                continue;

            _byPath.Remove(oldKey);
            _byPath[newKey] = state;
        }
    }

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
