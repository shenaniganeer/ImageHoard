namespace ImageHoard.Core.Services;

/// <summary>
/// Long-path normalization (NFR-SC-01) for Win32 I/O: \\?\ and \\?\UNC\ prefixes.
/// </summary>
public static class PathNormalizer
{
    /// <summary>
    /// Returns a path suitable for extended-length Win32 APIs. No-op if already prefixed.
    /// </summary>
    public static string ForIo(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        var trimmed = path.Trim();
        if (trimmed.StartsWith(@"\\?\", StringComparison.Ordinal))
            return trimmed;

        string full;
        try
        {
            full = Path.GetFullPath(trimmed);
        }
        catch
        {
            full = trimmed;
        }

        // UNC: \\server\share\... -> \\?\UNC\server\share\...
        if (full.StartsWith(@"\\", StringComparison.Ordinal) && !full.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            var uncBody = full.AsSpan(2); // drop leading \\
            return string.Concat(@"\\?\UNC\", uncBody);
        }

        return @"\\?\" + full;
    }

    /// <summary>
    /// Canonical path for <see cref="DirectoryInfo"/> enumeration. Does not add the <c>\\?\</c> prefix
    /// used by <see cref="ForIo"/>; extended-prefix directory enumeration has produced empty results in
    /// some WinUI hosts while canonical paths behave correctly. Continue to use <see cref="ForIo"/> for
    /// file moves, deletes, and existence checks where long-path Win32 semantics are required (NFR-SC-01).
    /// </summary>
    public static string ForDirectoryListing(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        var trimmed = path.Trim();
        if (trimmed.StartsWith(@"\\?\", StringComparison.Ordinal))
            return trimmed;

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return trimmed;
        }
    }
}
