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
                Directory.Move(
                    PathNormalizer.ForIo(sourceFullPath),
                    PathNormalizer.ForIo(destinationFullPath));
            },
            cancellationToken);
}
