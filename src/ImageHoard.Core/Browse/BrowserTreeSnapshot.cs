using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ImageHoard.Core.Browse;

/// <summary>
/// Pure helpers for persisted browser folder-tree snapshots (cold-boot restore, FR-BR-01 / FR-ST-02).
/// </summary>
public static class BrowserTreeSnapshot
{
    public const int MaxExpandedFolderPaths = 64;

    /// <summary>
    /// Snapshot is usable only when its capture root matches the active browse folder (caller should also verify directories exist on disk).
    /// </summary>
    public static bool IsRestoreRootMatching(string? snapshotBrowseRoot, string? lastBrowseFolder)
    {
        if (string.IsNullOrWhiteSpace(snapshotBrowseRoot) || string.IsNullOrWhiteSpace(lastBrowseFolder))
            return false;
        string a, b;
        try
        {
            a = Path.GetFullPath(snapshotBrowseRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            b = Path.GetFullPath(lastBrowseFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch
        {
            return false;
        }

        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    public static double? SanitizeStoredScroll(double? value)
    {
        if (value is not { } v || double.IsNaN(v) || double.IsInfinity(v) || v < 0)
            return null;
        return v;
    }

    /// <summary>Clamps a non-negative offset to <c>[0, scrollable]</c> where <paramref name="scrollable"/> is <c>max(0, extent - viewport)</c>.</summary>
    public static double ClampScrollOffset(double offset, double scrollable)
    {
        if (double.IsNaN(offset) || double.IsInfinity(offset))
            return 0;
        if (scrollable <= 0)
            return 0;
        if (offset < 0)
            return 0;
        return offset > scrollable ? scrollable : offset;
    }

    /// <summary>
    /// Returns paths strictly under <paramref name="browseRoot"/>, deduped case-insensitively, at most <paramref name="maxCount"/>.
    /// <paramref name="priorityFirst"/> is emitted first (e.g. ancestor chain toward the selected image), then <paramref name="rest"/> in enumeration order.
    /// </summary>
    public static List<string> MergePriorityThenCapDedupeUnderRoot(
        string browseRoot,
        IEnumerable<string> priorityFirst,
        IEnumerable<string> rest,
        int maxCount)
    {
        if (maxCount <= 0)
            return new List<string>();

        string root;
        try
        {
            root = Path.GetFullPath(browseRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch
        {
            return new List<string>();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(Math.Min(maxCount, MaxExpandedFolderPaths));

        void Consider(string? p)
        {
            if (result.Count >= maxCount || string.IsNullOrWhiteSpace(p))
                return;
            if (!BrowserTreeDeletePathDedupe.IsStrictDescendantPath(root, p))
                return;
            if (!seen.Add(p))
                return;
            result.Add(p);
        }

        foreach (var p in priorityFirst)
            Consider(p);
        foreach (var p in rest)
            Consider(p);

        return result;
    }

    /// <summary>Folder rename/move: same semantics as the WinUI host's path relocation under a renamed root.</summary>
    public static string RelocatePathUnderDirectoryRename(string fullPath, string oldRoot, string newRoot)
    {
        try
        {
            var fr = Path.GetFullPath(fullPath);
            var or = Path.GetFullPath(oldRoot);
            var nr = Path.GetFullPath(newRoot);
            var sep = Path.DirectorySeparatorChar;
            if (string.Equals(fr, or, StringComparison.OrdinalIgnoreCase))
                return nr;
            if (fr.StartsWith(or + sep, StringComparison.OrdinalIgnoreCase))
                return nr + fr.Substring(or.Length);
            if (fr.StartsWith(or + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return nr + fr.Substring(or.Length);
        }
        catch
        {
            // ignored
        }

        return fullPath;
    }

    public static List<string> RelocateExpandedPaths(IReadOnlyList<string> paths, string oldRoot, string newRoot)
    {
        var list = new List<string>(paths.Count);
        foreach (var p in paths)
            list.Add(RelocatePathUnderDirectoryRename(p, oldRoot, newRoot));
        return list;
    }

    /// <summary>Shallow-first expansion order: fewer directory segments from drive root first (stable tie-break).</summary>
    public static List<string> OrderExpandedPathsShallowFirst(IReadOnlyList<string> paths, string browseRoot)
    {
        string root;
        try
        {
            root = Path.GetFullPath(browseRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch
        {
            return paths.ToList();
        }

        static int Depth(string p)
        {
            try
            {
                var fp = Path.GetFullPath(p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var pr = Path.GetPathRoot(fp);
                if (string.IsNullOrEmpty(pr))
                    return int.MaxValue;
                var rel = Path.GetRelativePath(pr, fp);
                if (rel is "." or "")
                    return 0;
                return rel.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries).Length;
            }
            catch
            {
                return int.MaxValue;
            }
        }

        var rootDepth = Depth(root);
        return paths
            .OrderBy(p => Depth(p) - rootDepth)
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Ancestor directories from <paramref name="fileOrFolderPath"/> up to but excluding <paramref name="browseRoot"/> (strict descendants only).</summary>
    public static List<string> EnumerateAncestorFolderChain(string fileOrFolderPath, string browseRoot)
    {
        var chain = new List<string>();
        if (string.IsNullOrWhiteSpace(fileOrFolderPath) || string.IsNullOrWhiteSpace(browseRoot))
            return chain;

        string root;
        try
        {
            root = Path.GetFullPath(browseRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch
        {
            return chain;
        }

        string walk;
        try
        {
            var trimmed = fileOrFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (File.Exists(fileOrFolderPath))
                walk = Path.GetDirectoryName(fileOrFolderPath) ?? "";
            else if (Directory.Exists(fileOrFolderPath))
                walk = Path.GetFullPath(trimmed);
            else if (!string.IsNullOrEmpty(Path.GetFileName(trimmed)) && Path.HasExtension(trimmed))
                walk = Path.GetDirectoryName(fileOrFolderPath) ?? "";
            else
                return chain;
        }
        catch
        {
            return chain;
        }

        if (string.IsNullOrEmpty(walk))
            return chain;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!string.IsNullOrEmpty(walk))
        {
            if (!BrowserTreeDeletePathDedupe.IsStrictDescendantPath(root, walk))
                break;
            if (!seen.Add(walk))
                break;
            chain.Add(walk);
            try
            {
                walk = Path.GetDirectoryName(walk) ?? "";
            }
            catch
            {
                break;
            }
        }

        chain.Reverse();
        return chain;
    }
}
