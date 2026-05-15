using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ImageHoard.Core.Browse;

/// <summary>Deterministic path sets for browser tree multi-delete (FR-SR-08 / browse UX).</summary>
public static class BrowserTreeDeletePathDedupe
{
    /// <summary>Returns true when <paramref name="candidatePath"/> is a file or directory strictly under <paramref name="ancestorDirectory"/>.</summary>
    public static bool IsStrictDescendantPath(string ancestorDirectory, string candidatePath)
    {
        if (string.IsNullOrEmpty(ancestorDirectory) || string.IsNullOrEmpty(candidatePath))
            return false;
        string root;
        string cand;
        try
        {
            root = Path.GetFullPath(ancestorDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            cand = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return false;
        }

        if (string.Equals(root, cand, StringComparison.OrdinalIgnoreCase))
            return false;

        return cand.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || cand.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    static int DirectoryDepth(string path)
    {
        try
        {
            var p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(p))
                return 0;
            var root = Path.GetPathRoot(p);
            if (string.IsNullOrEmpty(root))
                return 0;
            var rel = Path.GetRelativePath(root, p);
            if (rel is "." or "")
                return 0;
            return rel.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries).Length;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Drops files under any selected directory and drops nested selected directories.
    /// Returns files to delete first, then folders deepest-first (parent after children).
    /// </summary>
    public static (List<string> Files, List<string> FoldersDeepestFirst) BuildDeletionPathLists(
        IEnumerable<string> filePaths,
        IEnumerable<string> directoryPaths)
    {
        var files = new HashSet<string>(filePaths.Where(p => !string.IsNullOrWhiteSpace(p)), StringComparer.OrdinalIgnoreCase);
        var dirs = new HashSet<string>(directoryPaths.Where(p => !string.IsNullOrWhiteSpace(p)), StringComparer.OrdinalIgnoreCase);

        var dirList = dirs.ToList();
        foreach (var d in dirList)
        {
            foreach (var other in dirList)
            {
                if (string.Equals(d, other, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (IsStrictDescendantPath(other, d))
                    dirs.Remove(d);
            }
        }

        var fileList = files.ToList();
        foreach (var f in fileList.ToList())
        {
            foreach (var d in dirs)
            {
                if (IsStrictDescendantPath(d, f))
                {
                    files.Remove(f);
                    break;
                }
            }
        }

        var folderOrdered = dirs.OrderByDescending(DirectoryDepth).ThenBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        return (files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList(), folderOrdered);
    }
}
