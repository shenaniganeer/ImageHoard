using ImageHoard.Core.Browse;
using ImageHoard.Core.Models;
using ImageHoard.Core.Services;

namespace ImageHoard.Core.Slideshow;

/// <summary>FR-SL-06 Folder scope — ordered sibling list (default name ascending).</summary>
public sealed class SiblingImageNavigator
{
    private readonly IReadOnlyList<string> _paths;
    private int _index;

    private SiblingImageNavigator(IReadOnlyList<string> paths, int startIndex)
    {
        _paths = paths;
        _index = Math.Clamp(startIndex, 0, Math.Max(0, paths.Count - 1));
    }

    public static async Task<SiblingImageNavigator?> CreateAsync(
        IFileSystem fileSystem,
        string imageFilePath,
        CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(imageFilePath);
        if (string.IsNullOrEmpty(dir))
            return null;

        IReadOnlyList<FileSystemEntry> entries;
        try
        {
            entries = await fileSystem.ListDirectoryAsync(dir, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        var imgs = entries
            .Where(e => !e.IsDirectory && ImageExtensions.IsImageFile(e.FullPath))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(e => e.FullPath)
            .ToList();

        if (imgs.Count == 0)
            return null;

        var norm = Path.GetFullPath(imageFilePath);
        var idx = imgs.FindIndex(p => string.Equals(Path.GetFullPath(p), norm, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
            idx = 0;

        return new SiblingImageNavigator(imgs, idx);
    }

    public string? CurrentPath => _paths.Count == 0 ? null : _paths[_index];

    public int Count => _paths.Count;

    public bool TryMoveNext(out string? path)
    {
        path = null;
        if (_paths.Count == 0)
            return false;

        if (_paths.Count == 1)
        {
            path = _paths[0];
            return true;
        }

        _index = (_index + 1) % _paths.Count;
        path = _paths[_index];
        return true;
    }

    public bool TryMovePrevious(out string? path)
    {
        path = null;
        if (_paths.Count == 0)
            return false;

        if (_paths.Count == 1)
        {
            path = _paths[0];
            return true;
        }

        _index = (_index - 1 + _paths.Count) % _paths.Count;
        path = _paths[_index];
        return true;
    }
}
