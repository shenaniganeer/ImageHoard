using ImageHoard.Core.Models;



namespace ImageHoard.Core.Services;



/// <summary>

/// Filesystem abstraction (NFR-AN-01: no Win32-only APIs in Core).

/// </summary>

public interface IFileSystem

{

    /// <summary>

    /// Lists immediate children of a directory. Directories first, then files; name order ascending (FR-BR-03 stable tiebreak).

    /// </summary>

    Task<IReadOnlyList<FileSystemEntry>> ListDirectoryAsync(

        string directoryPath,

        CancellationToken cancellationToken = default);



    Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default);



    Task<bool> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default);



    Task MoveFileAsync(

        string sourceFullPath,

        string destinationFullPath,

        bool overwrite = false,

        CancellationToken cancellationToken = default);



    /// <summary>Permanent delete (tests / internal). Recycle Bin is App-layer (FR-SR-08).</summary>

    Task DeleteFileAsync(string filePath, CancellationToken cancellationToken = default);



    Task MoveDirectoryAsync(

        string sourceFullPath,

        string destinationFullPath,

        CancellationToken cancellationToken = default);

}


