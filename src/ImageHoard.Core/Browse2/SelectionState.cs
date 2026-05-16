using ImageHoard.Core.Browse;

namespace ImageHoard.Core.Browse2;

/// <summary>Currently selected folder path for the browser tree (normalized when assigned).</summary>
public sealed class SelectionState
{
    private string? _selectedFolderPath;

    public string? SelectedFolderPath
    {
        get => _selectedFolderPath;
        set => _selectedFolderPath = string.IsNullOrWhiteSpace(value)
            ? null
            : FavoriteIndexRoots.NormalizeFavoritePath(value);
    }
}
