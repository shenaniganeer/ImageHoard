namespace ImageHoard.Core.Browse;

/// <summary>
/// Resolves the filesystem directory used for scoped image navigation (single-folder list),
/// using the same hint precedence as <see cref="BrowseSequentialNavIndex.ResolveCurrentIndex"/>.
/// </summary>
public static class BrowseContextDirectory
{
    /// <summary>
    /// Returns the directory containing the best-matching file hint, or <paramref name="browseRootFolderPath"/> when no file hint applies.
    /// </summary>
    public static string? Resolve(
        string? browseNavAnchorPath,
        string? treeSelectedImagePath,
        string? displayedImagePath,
        string? browseRootFolderPath)
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

        return NormalizeFolderPath(browseRootFolderPath);
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
