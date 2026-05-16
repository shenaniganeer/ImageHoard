namespace ImageHoard.Core.Browse;

/// <summary>Optional post-wizard browser refocus when a directory was removed and the next sibling should receive focus.</summary>
public readonly record struct BrowserTreeRefocusAfterWizardContext(
    string? PreferredNextFolderFullPath,
    string? ImageDeletionWorkingFolder = null);
