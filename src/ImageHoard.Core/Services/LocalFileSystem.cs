using System.Security.Cryptography;
using ImageHoard.Core.Models;

namespace ImageHoard.Core.Services;

public sealed class LocalFileSystem : IFileSystem
{
    public Task<IReadOnlyList<FileSystemEntry>> ListDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var trimmed = directoryPath.Trim();
                var listingPath = PathNormalizer.ForDirectoryListing(trimmed);
                if (!Directory.Exists(listingPath))
                {
                    var ioPath = PathNormalizer.ForIo(trimmed);
                    if (Directory.Exists(ioPath))
                        listingPath = ioPath;
                    else
                        throw new DirectoryNotFoundException(directoryPath);
                }

                var di = new DirectoryInfo(listingPath);
                if (!di.Exists)
                    throw new DirectoryNotFoundException(directoryPath);

                var dirs = new List<FileSystemEntry>();
                var files = new List<FileSystemEntry>();

                foreach (var item in di.EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var isReparse = (item.Attributes & FileAttributes.ReparsePoint) != 0;
                    if ((item.Attributes & FileAttributes.Directory) != 0)
                    {
                        dirs.Add(
                            new FileSystemEntry(
                                item.FullName,
                                item.Name,
                                IsDirectory: true,
                                LengthBytes: null,
                                LastWriteTimeUtc: new DateTimeOffset(item.LastWriteTimeUtc),
                                IsReparsePoint: isReparse));
                    }
                    else
                    {
                        var fi = (FileInfo)item;
                        files.Add(
                            new FileSystemEntry(
                                fi.FullName,
                                fi.Name,
                                IsDirectory: false,
                                fi.Length,
                                new DateTimeOffset(fi.LastWriteTimeUtc),
                                IsReparsePoint: false));
                    }
                }

                dirs.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                files.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

                var combined = new List<FileSystemEntry>(dirs.Count + files.Count);
                combined.AddRange(dirs);
                combined.AddRange(files);
                return (IReadOnlyList<FileSystemEntry>)combined;
            },
            cancellationToken);
    }

    public Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return File.Exists(PathNormalizer.ForIo(filePath));
            },
            cancellationToken);

    public Task<bool> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var t = directoryPath.Trim();
                return Directory.Exists(PathNormalizer.ForDirectoryListing(t))
                       || Directory.Exists(PathNormalizer.ForIo(t));
            },
            cancellationToken);

    public Task MoveFileAsync(
        string sourceFullPath,
        string destinationFullPath,
        bool overwrite = false,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(
                    PathNormalizer.ForIo(sourceFullPath),
                    PathNormalizer.ForIo(destinationFullPath),
                    overwrite);
            },
            cancellationToken);

    public Task DeleteFileAsync(string filePath, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Delete(PathNormalizer.ForIo(filePath));
            },
            cancellationToken);

    public Task MoveDirectoryAsync(
        string sourceFullPath,
        string destinationFullPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                MoveDirectorySync(sourceFullPath, destinationFullPath, cancellationToken);
            },
            cancellationToken);

    public Task MergeMoveDirectoryAsync(
        string sourceFullPath,
        string destinationFullPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                MergeMoveDirectorySync(sourceFullPath, destinationFullPath, cancellationToken);
            },
            cancellationToken);

    private void MergeMoveDirectorySync(
        string sourceFullPath,
        string destinationFullPath,
        CancellationToken cancellationToken)
    {
        var srcTrim = sourceFullPath.Trim();
        var dstTrim = destinationFullPath.Trim();

        if (!TryResolveExistingDirectoryListingPath(srcTrim, out var srcListing))
            throw new DirectoryNotFoundException(sourceFullPath);

        if (!DirectoryExistsEitherForm(dstTrim))
        {
            MoveDirectorySync(sourceFullPath, destinationFullPath, cancellationToken);
            return;
        }

        if (!TryResolveExistingDirectoryListingPath(dstTrim, out var dstListing))
        {
            throw new IOException(
                "ImageHoard merge: destination path exists but is not a directory. Resolve manually before moving to archive.");
        }

        var srcVisited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dstVisited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        PreflightMergeRecursive(
            srcListing,
            dstListing,
            symlinkDepthFromRoot: 0,
            srcVisited,
            dstVisited,
            cancellationToken);

        srcVisited.Clear();
        dstVisited.Clear();
        ExecuteMergeRecursive(
            srcListing,
            dstListing,
            symlinkDepthFromRoot: 0,
            srcVisited,
            dstVisited,
            cancellationToken);

        var srcIo = PathNormalizer.ForIo(srcTrim);
        if (Directory.Exists(srcIo))
            Directory.Delete(srcIo, recursive: true);
    }

    private void PreflightMergeRecursive(
        string sourceListingPath,
        string destListingPath,
        int symlinkDepthFromRoot,
        HashSet<string> srcVisited,
        HashSet<string> dstVisited,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var srcKey = NormalizeVisitedKey(sourceListingPath);
        if (!srcVisited.Add(srcKey))
            return;

        var dstKey = NormalizeVisitedKey(destListingPath);
        if (!dstVisited.Add(dstKey))
            return;

        var srcEntries = ListDirectoryAsync(sourceListingPath, cancellationToken).GetAwaiter().GetResult();
        var dstEntries = ListDirectoryAsync(destListingPath, cancellationToken).GetAwaiter().GetResult();
        var dstByName = new Dictionary<string, FileSystemEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in dstEntries)
            dstByName[d.Name] = d;

        foreach (var srcEntry in srcEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            dstByName.TryGetValue(srcEntry.Name, out var dstMatch);

            if (dstMatch == null)
            {
                if (srcEntry.IsDirectory)
                {
                    var nextDepth = symlinkDepthFromRoot + (srcEntry.IsReparsePoint ? 1 : 0);
                    if (srcEntry.IsReparsePoint && nextDepth > SymlinkTraversalPolicy.MaxSymlinkDepthFromRoot)
                        continue;

                    PreflightSourceSubtreeNoDestOverlap(srcEntry.FullPath, nextDepth, srcVisited, cancellationToken);
                }

                continue;
            }

            if (srcEntry.IsDirectory != dstMatch.IsDirectory)
            {
                throw new IOException(
                    "ImageHoard merge: a file and a directory share the same name in the browse folder and archive. Resolve manually before moving to archive.");
            }

            if (srcEntry.IsDirectory)
            {
                var nextDepth = symlinkDepthFromRoot + (srcEntry.IsReparsePoint ? 1 : 0);
                if (srcEntry.IsReparsePoint && nextDepth > SymlinkTraversalPolicy.MaxSymlinkDepthFromRoot)
                    continue;

                PreflightMergeRecursive(
                    srcEntry.FullPath,
                    dstMatch.FullPath,
                    nextDepth,
                    srcVisited,
                    dstVisited,
                    cancellationToken);
            }
            else
            {
                if (!FilesHaveEqualSha256(srcEntry.FullPath, dstMatch.FullPath, cancellationToken))
                {
                    throw new IOException(
                        "ImageHoard merge: conflicting files with the same relative path but different content. Resolve manually before moving to archive.");
                }
            }
        }
    }

    private void PreflightSourceSubtreeNoDestOverlap(
        string sourceDirListingPath,
        int symlinkDepthFromRoot,
        HashSet<string> visitedDirs,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = NormalizeVisitedKey(sourceDirListingPath);
        if (!visitedDirs.Add(key))
            return;

        var entries = ListDirectoryAsync(sourceDirListingPath, cancellationToken).GetAwaiter().GetResult();
        foreach (var entry in entries.Where(e => e.IsDirectory))
        {
            var nextDepth = symlinkDepthFromRoot + (entry.IsReparsePoint ? 1 : 0);
            if (entry.IsReparsePoint && nextDepth > SymlinkTraversalPolicy.MaxSymlinkDepthFromRoot)
                continue;

            PreflightSourceSubtreeNoDestOverlap(entry.FullPath, nextDepth, visitedDirs, cancellationToken);
        }
    }

    private void ExecuteMergeRecursive(
        string sourceListingPath,
        string destListingPath,
        int symlinkDepthFromRoot,
        HashSet<string> srcVisited,
        HashSet<string> dstVisited,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var srcKey = NormalizeVisitedKey(sourceListingPath);
        if (!srcVisited.Add(srcKey))
            return;

        var dstKey = NormalizeVisitedKey(destListingPath);
        if (!dstVisited.Add(dstKey))
            return;

        var srcEntries = ListDirectoryAsync(sourceListingPath, cancellationToken).GetAwaiter().GetResult();
        var dstEntries = ListDirectoryAsync(destListingPath, cancellationToken).GetAwaiter().GetResult();
        var dstByName = new Dictionary<string, FileSystemEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in dstEntries)
            dstByName[d.Name] = d;

        foreach (var srcEntry in srcEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            dstByName.TryGetValue(srcEntry.Name, out var dstMatch);

            if (dstMatch == null)
            {
                if (srcEntry.IsDirectory)
                {
                    var nextDepth = symlinkDepthFromRoot + (srcEntry.IsReparsePoint ? 1 : 0);
                    if (srcEntry.IsReparsePoint && nextDepth > SymlinkTraversalPolicy.MaxSymlinkDepthFromRoot)
                        continue;

                    var destChildTrim = CombineListingChildPath(destListingPath, srcEntry.Name);
                    MoveDirectorySync(srcEntry.FullPath.Trim(), destChildTrim, cancellationToken);
                }
                else
                {
                    var destFileTrim = CombineListingChildPath(destListingPath, srcEntry.Name);
                    MoveOrCopyFileNoOverwrite(srcEntry.FullPath, destFileTrim, cancellationToken);
                }

                continue;
            }

            if (srcEntry.IsDirectory != dstMatch.IsDirectory)
            {
                throw new IOException(
                    "ImageHoard merge: a file and a directory share the same name in the browse folder and archive. Resolve manually before moving to archive.");
            }

            if (srcEntry.IsDirectory)
            {
                var nextDepth = symlinkDepthFromRoot + (srcEntry.IsReparsePoint ? 1 : 0);
                if (srcEntry.IsReparsePoint && nextDepth > SymlinkTraversalPolicy.MaxSymlinkDepthFromRoot)
                    continue;

                ExecuteMergeRecursive(
                    srcEntry.FullPath,
                    dstMatch.FullPath,
                    nextDepth,
                    srcVisited,
                    dstVisited,
                    cancellationToken);
                TryDeleteDirectoryTreeIfEmpty(PathNormalizer.ForIo(srcEntry.FullPath.Trim()));
            }
            else
            {
                File.Delete(PathNormalizer.ForIo(srcEntry.FullPath));
            }
        }
    }

    private static string CombineListingChildPath(string parentListingPath, string childName)
    {
        var parent = parentListingPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        try
        {
            return Path.GetFullPath(Path.Combine(parent, childName));
        }
        catch
        {
            return Path.Combine(parent, childName);
        }
    }

    private static void MoveOrCopyFileNoOverwrite(string sourceFilePath, string destFilePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var srcIo = PathNormalizer.ForIo(sourceFilePath.Trim());
        var dstIo = PathNormalizer.ForIo(destFilePath.Trim());
        if (File.Exists(dstIo))
            throw new IOException("ImageHoard merge: unexpected existing destination file.");

        string srcDir;
        string dstDir;
        try
        {
            srcDir = Path.GetDirectoryName(Path.GetFullPath(sourceFilePath.Trim()))
                     ?? sourceFilePath.Trim();
        }
        catch
        {
            srcDir = sourceFilePath.Trim();
        }

        try
        {
            dstDir = Path.GetDirectoryName(Path.GetFullPath(destFilePath.Trim()))
                     ?? destFilePath.Trim();
        }
        catch
        {
            dstDir = destFilePath.Trim();
        }

        if (SameVolumeRoot(srcDir, dstDir))
            File.Move(srcIo, dstIo);
        else
        {
            File.Copy(srcIo, dstIo, overwrite: false);
            File.Delete(srcIo);
        }
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

    private static void TryDeleteDirectoryTreeIfEmpty(string directoryIoPath)
    {
        if (!Directory.Exists(directoryIoPath))
            return;

        foreach (var sub in Directory.GetDirectories(directoryIoPath))
            TryDeleteDirectoryTreeIfEmpty(sub);

        if (Directory.GetFiles(directoryIoPath).Length == 0 && Directory.GetDirectories(directoryIoPath).Length == 0)
            Directory.Delete(directoryIoPath, recursive: false);
    }

    private void MoveDirectorySync(
        string sourceFullPath,
        string destinationFullPath,
        CancellationToken cancellationToken)
    {
        var srcTrim = sourceFullPath.Trim();
        var dstTrim = destinationFullPath.Trim();

        if (!TryResolveExistingDirectoryListingPath(srcTrim, out var srcListing))
            throw new DirectoryNotFoundException(sourceFullPath);

        if (DirectoryExistsEitherForm(dstTrim))
        {
            throw new IOException(
                "Cannot create a file when that file already exists. "
                + "The destination directory already exists or conflicts with an existing path.");
        }

        if (SameVolumeRoot(srcListing, dstTrim))
        {
            Directory.Move(PathNormalizer.ForIo(srcTrim), PathNormalizer.ForIo(dstTrim));
            return;
        }

        try
        {
            Directory.CreateDirectory(PathNormalizer.ForIo(dstTrim));
            CopyDirectoryContentsAcrossVolumes(
                srcListing,
                dstTrim,
                symlinkDepthFromRoot: 0,
                visitedDirs: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                cancellationToken);
        }
        catch
        {
            TryDeleteDirectoryRecursiveBestEffort(dstTrim);
            throw;
        }

        Directory.Delete(PathNormalizer.ForIo(srcTrim), recursive: true);
    }

    private void CopyDirectoryContentsAcrossVolumes(
        string sourceDirListingPath,
        string destDirPath,
        int symlinkDepthFromRoot,
        HashSet<string> visitedDirs,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dirKey = NormalizeVisitedKey(sourceDirListingPath);
        if (!visitedDirs.Add(dirKey))
            return;

        var entries = ListDirectoryAsync(sourceDirListingPath, cancellationToken).GetAwaiter().GetResult();

        foreach (var entry in entries.Where(e => e.IsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nextDepth = symlinkDepthFromRoot + (entry.IsReparsePoint ? 1 : 0);
            if (entry.IsReparsePoint && nextDepth > SymlinkTraversalPolicy.MaxSymlinkDepthFromRoot)
                continue;

            var childDest = Path.Combine(destDirPath, entry.Name);
            Directory.CreateDirectory(PathNormalizer.ForIo(childDest));
            CopyDirectoryContentsAcrossVolumes(
                entry.FullPath,
                childDest,
                nextDepth,
                visitedDirs,
                cancellationToken);
        }

        foreach (var entry in entries.Where(e => !e.IsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destFile = Path.Combine(destDirPath, entry.Name);
            File.Copy(
                PathNormalizer.ForIo(entry.FullPath),
                PathNormalizer.ForIo(destFile),
                overwrite: false);
        }
    }

    private static bool TryResolveExistingDirectoryListingPath(string trimmedPath, out string listingPath)
    {
        listingPath = PathNormalizer.ForDirectoryListing(trimmedPath);
        if (Directory.Exists(listingPath))
            return true;

        var ioPath = PathNormalizer.ForIo(trimmedPath);
        if (!Directory.Exists(ioPath))
            return false;

        listingPath = ioPath;
        return true;
    }

    private static bool DirectoryExistsEitherForm(string trimmedPath) =>
        Directory.Exists(PathNormalizer.ForDirectoryListing(trimmedPath))
        || Directory.Exists(PathNormalizer.ForIo(trimmedPath));

    private static bool SameVolumeRoot(string pathA, string pathB)
    {
        string fullA;
        string fullB;
        try
        {
            fullA = Path.GetFullPath(pathA.Trim());
        }
        catch
        {
            return false;
        }

        try
        {
            fullB = Path.GetFullPath(pathB.Trim());
        }
        catch
        {
            return false;
        }

        var rootA = Path.GetPathRoot(fullA);
        var rootB = Path.GetPathRoot(fullB);
        if (string.IsNullOrEmpty(rootA) || string.IsNullOrEmpty(rootB))
            return false;

        return string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVisitedKey(string path)
    {
        try
        {
            return Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch
        {
            return path;
        }
    }

    private static void TryDeleteDirectoryRecursiveBestEffort(string trimmedDestinationPath)
    {
        try
        {
            var io = PathNormalizer.ForIo(trimmedDestinationPath);
            if (Directory.Exists(io))
                Directory.Delete(io, recursive: true);
        }
        catch
        {
            // Best-effort cleanup after a failed cross-volume copy.
        }
    }
}
