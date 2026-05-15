using ImageHoard.Core.Services;

namespace ImageHoard.Core.Slideshow;

/// <summary>Coordinates tree random session with optional in-slideshow sibling navigation (overlay).</summary>
public sealed class SlideshowCoordinator
{
    private readonly TreeSlideshowSession _tree;
    private SiblingImageNavigator? _siblingNav;

    public SlideshowCoordinator(TreeSlideshowSession treeSession)
    {
        _tree = treeSession;
    }

    public TreeSlideshowSession Tree => _tree;

    /// <summary>True when next/prev for siblings are active; tree reservoir is unchanged.</summary>
    public bool IsSiblingOverlayActive => _siblingNav != null;

    public void ClearSiblingOverlay() => _siblingNav = null;

    /// <summary>Random tree next; clears sibling overlay.</summary>
    public bool TryMoveNextTree(out string? path)
    {
        _siblingNav = null;
        return _tree.TryMoveNext(out path);
    }

    /// <summary>Random tree previous; clears sibling overlay.</summary>
    public bool TryMovePreviousTree(out string? path)
    {
        _siblingNav = null;
        return _tree.TryMovePrevious(out path);
    }

    public async Task<bool> TryMoveNextSiblingAsync(
        IFileSystem fileSystem,
        string? displayedImagePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(displayedImagePath))
            return false;

        if (_siblingNav == null)
        {
            var nav = await SiblingImageNavigator.CreateAsync(fileSystem, displayedImagePath, cancellationToken)
                .ConfigureAwait(false);
            if (nav == null)
                return false;
            _siblingNav = nav;
        }

        return _siblingNav.TryMoveNext(out _);
    }

    public async Task<bool> TryMovePreviousSiblingAsync(
        IFileSystem fileSystem,
        string? displayedImagePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(displayedImagePath))
            return false;

        if (_siblingNav == null)
        {
            var nav = await SiblingImageNavigator.CreateAsync(fileSystem, displayedImagePath, cancellationToken)
                .ConfigureAwait(false);
            if (nav == null)
                return false;
            _siblingNav = nav;
        }

        return _siblingNav.TryMovePrevious(out _);
    }

    public string? GetCurrentPath() =>
        _siblingNav != null ? _siblingNav.CurrentPath : _tree.CurrentPath;

    /// <summary>
    /// List position for the path overlay: tree uses session history index and discovered count;
    /// sibling overlay uses ordered sibling index in the image's directory.
    /// </summary>
    public bool TryGetSlideshowOverlayListPosition(out int index1Based, out int total, out bool enumerationComplete)
    {
        if (_siblingNav != null)
        {
            enumerationComplete = true;
            return _siblingNav.TryGetFolderPosition(out index1Based, out total);
        }

        enumerationComplete = _tree.IsEnumerationComplete;
        return _tree.TryGetTreeOverlayPosition(out index1Based, out total);
    }
}
