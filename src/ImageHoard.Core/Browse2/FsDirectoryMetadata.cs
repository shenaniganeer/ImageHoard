using ImageHoard.Core.Browse;

namespace ImageHoard.Core.Browse2;

/// <summary>Best-effort directory timestamps (BCL; not Win32-specific).</summary>
internal static class FsDirectoryMetadata
{
    public static DateTimeOffset? TryGetLastWriteTimeUtc(string directoryPath)
    {
        try
        {
            var n = FavoriteIndexRoots.NormalizeFavoritePath(directoryPath);
            if (string.IsNullOrEmpty(n) || !Directory.Exists(n))
                return null;
            return new DateTimeOffset(Directory.GetLastWriteTimeUtc(n), TimeSpan.Zero);
        }
        catch
        {
            return null;
        }
    }
}
