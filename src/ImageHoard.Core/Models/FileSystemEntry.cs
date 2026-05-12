namespace ImageHoard.Core.Models;

/// <summary>
/// One row from a directory listing (folder or file).
/// </summary>
public sealed record FileSystemEntry(
    string FullPath,
    string Name,
    bool IsDirectory,
    long? LengthBytes,
    DateTimeOffset? LastWriteTimeUtc,
    bool IsReparsePoint = false);
