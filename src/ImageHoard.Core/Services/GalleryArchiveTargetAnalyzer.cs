using System.Security.Cryptography;
using ImageHoard.Core.Models;

namespace ImageHoard.Core.Services;

/// <summary>
/// Preview of how a browse gallery folder relates to the archive path where move-to-archive would land.
/// </summary>
public sealed record GalleryArchiveTargetPreview(
    bool DestExists,
    bool SourceHasImmediateSubfolders,
    bool HasContentConflict,
    bool HasIdenticalFileOverlap);

public static class GalleryArchiveTargetAnalyzer
{
    /// <summary>
    /// When <paramref name="workingFolder"/> has immediate child directories, collision fields are false
    /// (move-to-archive is blocked; overlay should not imply merge will run).
    /// </summary>
    public static async Task<GalleryArchiveTargetPreview> AnalyzeAsync(
        IFileSystem fileSystem,
        string? archiveRoot,
        string? workingFolder,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(archiveRoot)
            || string.IsNullOrWhiteSpace(workingFolder)
            || !await fileSystem.DirectoryExistsAsync(workingFolder, cancellationToken).ConfigureAwait(false))
        {
            return new GalleryArchiveTargetPreview(false, false, false, false);
        }

        var dest = Path.Combine(archiveRoot.Trim(), new DirectoryInfo(workingFolder.Trim()).Name);
        var destExists = await fileSystem.DirectoryExistsAsync(dest, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<FileSystemEntry> workEntries;
        try
        {
            workEntries = await fileSystem.ListDirectoryAsync(workingFolder, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return new GalleryArchiveTargetPreview(destExists, false, false, false);
        }

        var sourceHasSubfolders = workEntries.Any(e => e.IsDirectory);
        if (sourceHasSubfolders)
            return new GalleryArchiveTargetPreview(destExists, true, false, false);

        if (!destExists)
            return new GalleryArchiveTargetPreview(false, false, false, false);

        IReadOnlyList<FileSystemEntry> destEntries;
        try
        {
            destEntries = await fileSystem.ListDirectoryAsync(dest, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return new GalleryArchiveTargetPreview(true, false, false, false);
        }

        var destByName = new Dictionary<string, FileSystemEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in destEntries)
            destByName[d.Name] = d;

        var hasConflict = false;
        var hasIdentical = false;

        foreach (var entry in workEntries.Where(e => !e.IsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!destByName.TryGetValue(entry.Name, out var destEntry))
                continue;

            if (destEntry.IsDirectory)
            {
                hasConflict = true;
                continue;
            }

            if (FilesHaveEqualSha256(entry.FullPath, destEntry.FullPath, cancellationToken))
                hasIdentical = true;
            else
                hasConflict = true;
        }

        return new GalleryArchiveTargetPreview(true, false, hasConflict, hasIdentical);
    }

    private static bool FilesHaveEqualSha256(string pathA, string pathB, CancellationToken cancellationToken)
    {
        var ha = ComputeSha256(pathA, cancellationToken);
        var hb = ComputeSha256(pathB, cancellationToken);
        return CryptographicOperations.FixedTimeEquals(ha, hb);
    }

    private static byte[] ComputeSha256(string filePath, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(PathNormalizer.ForIo(filePath.Trim()));
        using var sha = SHA256.Create();
        return sha.ComputeHash(stream);
    }
}
