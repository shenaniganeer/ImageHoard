namespace ImageHoard.Core.Browse;

/// <summary>
/// Snapshot of browse + tree selection hints used to resolve viewport targets (mirrors WinUI host fields).
/// </summary>
/// <param name="CurrentFolderPath">Active browse root (<c>_currentFolderPath</c>).</param>
/// <param name="BrowseNavAnchorPath">Sequential navigation anchor file path when set.</param>
/// <param name="LastSelectedImage">Last committed image selection (session); not fed into <see cref="BrowseContextDirectory.Resolve"/> unless the host maps it elsewhere.</param>
/// <param name="CurrentImageFullPath">Image currently shown in the preview pane.</param>
/// <param name="TreeSelectedFolderPath">Folder path when the tree primary selection is a folder row.</param>
/// <param name="TreeSelectedImagePath">Image full path when the tree primary selection is an image row.</param>
public readonly record struct BrowserPaneState(
    string? CurrentFolderPath,
    string? BrowseNavAnchorPath,
    string? LastSelectedImage,
    string? CurrentImageFullPath,
    string? TreeSelectedFolderPath,
    string? TreeSelectedImagePath = null);
