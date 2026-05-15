using ImageHoard.Core.Services;

namespace ImageHoard.Core.Slideshow;

/// <summary>FR-SL-06/07 — coordinates Tree random session vs Folder sibling navigation.</summary>
public sealed class SlideshowCoordinator
{
    private readonly TreeSlideshowSession _tree;
    private SiblingImageNavigator? _folderNav;
    private SlideshowScopeKind _scope = SlideshowScopeKind.Tree;

    public SlideshowCoordinator(TreeSlideshowSession treeSession)
    {
        _tree = treeSession;
    }

    public SlideshowScopeKind Scope => _scope;

    public TreeSlideshowSession Tree => _tree;

    public async Task ToggleScopeAsync(
        IFileSystem fileSystem,
        string? currentImagePath,
        CancellationToken cancellationToken = default)
    {
        if (_scope == SlideshowScopeKind.Tree)
        {
            if (string.IsNullOrEmpty(currentImagePath))
                return;

            _folderNav = await SiblingImageNavigator.CreateAsync(fileSystem, currentImagePath, cancellationToken)
                .ConfigureAwait(false);
            _scope = SlideshowScopeKind.Folder;
        }
        else
        {
            _folderNav = null;
            _scope = SlideshowScopeKind.Tree;
        }
    }

    public bool TryMoveNext(out string? path)
    {
        if (_scope == SlideshowScopeKind.Folder && _folderNav != null)
            return _folderNav.TryMoveNext(out path);

        return _tree.TryMoveNext(out path);
    }

    public bool TryMovePrevious(out string? path)
    {
        if (_scope == SlideshowScopeKind.Folder && _folderNav != null)
            return _folderNav.TryMovePrevious(out path);

        return _tree.TryMovePrevious(out path);
    }

    public string? GetCurrentPath() =>
        _scope == SlideshowScopeKind.Folder && _folderNav != null
            ? _folderNav.CurrentPath
            : _tree.CurrentPath;

    /// <summary>
    /// List position for the path overlay: tree scope uses session history index and discovered count;
    /// folder scope uses ordered sibling index in the image's directory.
    /// </summary>
    public bool TryGetSlideshowOverlayListPosition(out int index1Based, out int total, out bool enumerationComplete)
    {
        if (_scope == SlideshowScopeKind.Folder && _folderNav != null)
        {
            enumerationComplete = true;
            return _folderNav.TryGetFolderPosition(out index1Based, out total);
        }

        enumerationComplete = _tree.IsEnumerationComplete;
        return _tree.TryGetTreeOverlayPosition(out index1Based, out total);
    }
}
