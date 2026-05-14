namespace ImageHoard.Core.Browse;

/// <summary>Folder/file name matching for Browse → Find in tree (FR-BR / FR-IN-01).</summary>
public static class BrowserFindNameMatching
{
    /// <summary>Returns false if <paramref name="query"/> is null/whitespace or <paramref name="name"/> is null/empty.</summary>
    public static bool NameMatches(string? query, string? name, bool matchFromStartOfName)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrEmpty(name))
            return false;
        var q = query.Trim();
        return matchFromStartOfName
            ? name.StartsWith(q, StringComparison.OrdinalIgnoreCase)
            : name.Contains(q, StringComparison.OrdinalIgnoreCase);
    }
}
