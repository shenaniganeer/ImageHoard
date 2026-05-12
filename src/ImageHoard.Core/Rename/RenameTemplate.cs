using System.Text.RegularExpressions;

namespace ImageHoard.Core.Rename;

public enum RenameCollisionPolicy
{
    Abort,
    Skip,
    AutoSuffix,
}

public static class RenameTemplate
{
    /// <summary>Apply v1 tokens for dry-run preview (FR-SR-06).</summary>
    public static string ApplyTemplate(
        string template,
        string sourceFilePath,
        int sequenceIndex,
        DateTime? dateTakenUtcOrNull)
    {
        var dir = Path.GetDirectoryName(sourceFilePath) ?? string.Empty;
        var parent = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var original = Path.GetFileNameWithoutExtension(sourceFilePath);
        var ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        var fullName = Path.GetFileName(sourceFilePath);
        var mtime = File.GetLastWriteTimeUtc(sourceFilePath);

        var dateTaken = dateTakenUtcOrNull.HasValue
            ? dateTakenUtcOrNull.Value.ToString("yyyy-MM-dd")
            : string.Empty;

        var result = template
            .Replace("{{", "\u0000brace\u0000")
            .Replace("}}", "\u0000braceClose\u0000")
            .Replace("{OriginalName}", original)
            .Replace("{OriginalNameFull}", fullName)
            .Replace("{Ext}", ext)
            .Replace("{ParentFolder}", parent)
            .Replace("{DateModified}", mtime.ToString("yyyy-MM-dd"))
            .Replace("{DateTaken}", dateTaken);

        result = Regex.Replace(
            result,
            @"\{Seq:(\d+)\}",
            m =>
            {
                var w = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                return sequenceIndex.ToString().PadLeft(w, '0');
            });

        result = result.Replace("\u0000brace\u0000", "{").Replace("\u0000braceClose\u0000", "}");
        return SanitizeFileName(result);
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        foreach (var t in "\\/:*?\"<>|")
        {
            for (var i = 0; i < chars.Length; i++)
            {
                if (chars[i] == t)
                    chars[i] = '_';
            }
        }

        for (var i = 0; i < chars.Length; i++)
        {
            if (invalid.Contains(chars[i]))
                chars[i] = '_';
        }

        var s = new string(chars).TrimEnd(' ', '.');
        return string.IsNullOrEmpty(s) ? "renamed" : s;
    }

    public static IReadOnlyList<RenamePreviewRow> BuildPreview(
        IReadOnlyList<string> sourcePaths,
        string template,
        string targetDirectory,
        RenameCollisionPolicy policy)
    {
        var rows = new List<RenamePreviewRow>();
        var usedDest = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = sourcePaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        var i = 0;
        foreach (var src in ordered)
        {
            i++;
            var baseName = ApplyTemplate(template, src, i, null);
            var ext = Path.GetExtension(src);
            if (!baseName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                baseName += ext;

            var dest = Path.Combine(targetDirectory, baseName);
            var status = RenamePreviewStatus.Ok;
            if (usedDest.Contains(dest))
            {
                if (policy == RenameCollisionPolicy.AutoSuffix)
                {
                    dest = MakeUniquePath(dest, usedDest);
                    baseName = Path.GetFileName(dest);
                }
                else
                {
                    status = RenamePreviewStatus.Collision;
                }
            }

            usedDest.Add(dest);
            rows.Add(new RenamePreviewRow(src, dest, status));
        }

        return rows;
    }

    private static string MakeUniquePath(string dest, HashSet<string> used)
    {
        var dir = Path.GetDirectoryName(dest) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(dest);
        var ext = Path.GetExtension(dest);
        for (var n = 2; n < 10_000; n++)
        {
            var candidate = Path.Combine(dir, $"{name} ({n}){ext}");
            if (!used.Contains(candidate))
                return candidate;
        }

        return dest;
    }
}

public enum RenamePreviewStatus
{
    Ok,
    Collision,
    Invalid,
}

public sealed record RenamePreviewRow(string SourcePath, string DestinationPath, RenamePreviewStatus Status);
