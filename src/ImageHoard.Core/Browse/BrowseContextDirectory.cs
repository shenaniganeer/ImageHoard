namespace ImageHoard.Core.Browse;

/// <summary>
/// Resolves the filesystem directory used for scoped image navigation (single-folder list),
/// using the same hint precedence as <see cref="BrowseSequentialNavIndex.ResolveCurrentIndex"/>.
/// </summary>
public static class BrowseContextDirectory
{
    /// <summary>
    /// Returns the directory containing the best-matching file hint, or the selected folder under the browse root
    /// when <paramref name="treeSelectedFolderPath"/> is under <paramref name="browseRootFolderPath"/>, else
    /// <paramref name="browseRootFolderPath"/> when no file hint applies.
    /// </summary>
    public static string? Resolve(
        string? browseNavAnchorPath,
        string? treeSelectedImagePath,
        string? displayedImagePath,
        string? browseRootFolderPath,
        string? treeSelectedFolderPath = null)
    {
        var d = DirectoryFromFilePath(browseNavAnchorPath);
        if (!string.IsNullOrEmpty(d))
            return d;

        d = DirectoryFromFilePath(treeSelectedImagePath);
        if (!string.IsNullOrEmpty(d))
            return d;

        d = DirectoryFromFilePath(displayedImagePath);
        if (!string.IsNullOrEmpty(d))
            return d;

        var rootNorm = NormalizeFolderPath(browseRootFolderPath);
        var folderNorm = NormalizeFolderPath(treeSelectedFolderPath);
        if (!string.IsNullOrEmpty(folderNorm)
            && !string.IsNullOrEmpty(rootNorm)
            && BrowseContextImageSequence.IsContextDirectoryUnderBrowseRoot(rootNorm, folderNorm))
            return folderNorm;

        return rootNorm;
    }

    private static string? DirectoryFromFilePath(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return null;

        string fullFile;
        try
        {
            fullFile = Path.GetFullPath(filePath);
        }
        catch
        {
            fullFile = filePath;
        }

        var dir = Path.GetDirectoryName(fullFile);
        return string.IsNullOrEmpty(dir) ? null : NormalizeFolderPath(dir);
    }

    private static string? NormalizeFolderPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }
}
