using System.Collections.Generic;
using System.IO;

namespace ImageHoard.Core.Browse;

/// <summary>Path-only validation for browser drag-move (no I/O).</summary>
public static class BrowserPaneMovePathValidation
{
    /// <param name="sources">Normalized full paths; <paramref name="isDirectory"/> from caller.</param>
    /// <param name="destinationDirectory">Normalized directory path.</param>
    /// <returns>Human-readable block reason, or null when the operation may proceed (subject to disk checks).</returns>
    public static string? GetBlockingReason(
        IReadOnlyList<(string Path, bool IsDirectory)> sources,
        string destinationDirectory)
    {
        if (sources.Count == 0)
            return "Nothing to move.";

        var dest = destinationDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(dest))
            return "Invalid destination folder.";

        var dirPaths = new List<string>();
        foreach (var (path, isDir) in sources)
        {
            if (isDir)
                dirPaths.Add(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        for (var i = 0; i < dirPaths.Count; i++)
        {
            for (var j = 0; j < dirPaths.Count; j++)
            {
                if (i == j)
                    continue;
                if (BrowserTreeDeletePathDedupe.IsStrictDescendantPath(dirPaths[i], dirPaths[j])
                    || BrowserTreeDeletePathDedupe.IsStrictDescendantPath(dirPaths[j], dirPaths[i]))
                    return "Cannot move a folder together with a selected folder inside it.";
            }
        }

        foreach (var (path, _) in sources)
        {
            var p = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(p, dest, StringComparison.OrdinalIgnoreCase))
                return "Cannot drop an item onto itself.";
        }

        foreach (var (path, isDir) in sources)
        {
            var p = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parent = Path.GetDirectoryName(p);
            if (!string.IsNullOrEmpty(parent)
                && string.Equals(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), dest, StringComparison.OrdinalIgnoreCase))
                return "One or more items are already in that folder.";

            if (isDir)
            {
                if (string.Equals(p, dest, StringComparison.OrdinalIgnoreCase))
                    return "Cannot move a folder into itself.";
                if (BrowserTreeDeletePathDedupe.IsStrictDescendantPath(p, dest))
                    return "Cannot move a folder into one of its subfolders.";
            }
        }

        return null;
    }
}
