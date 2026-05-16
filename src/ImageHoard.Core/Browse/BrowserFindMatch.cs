namespace ImageHoard.Core.Browse;

public enum BrowserFindMatchKind
{
    Folder,
    File,
}

/// <summary>One find-in-tree hit (shallow or deep search).</summary>
public readonly record struct BrowserFindMatch(string Path, string DisplayName, BrowserFindMatchKind Kind);
