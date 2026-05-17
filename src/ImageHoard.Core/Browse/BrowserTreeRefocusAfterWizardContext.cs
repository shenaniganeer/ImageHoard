namespace ImageHoard.Core.Browse;

/// <summary>Optional post-wizard browser refocus when a directory was removed and the next sibling should receive focus,
/// or when image deletes should advance selection within the same folder.</summary>
public readonly record struct BrowserTreeRefocusAfterWizardContext(
    string? PreferredNextFolderFullPath = null,
    string? ImageDeletionWorkingFolder = null,
    IReadOnlyList<string>? DeletedImagePathsForRefocus = null,
    IReadOnlyList<string>? ImagePanePathsBeforeDeletion = null);
