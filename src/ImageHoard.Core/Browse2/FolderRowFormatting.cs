namespace ImageHoard.Core.Browse2;

/// <summary>User-visible strings for <see cref="FolderRow"/> metrics (Browse2 tree columns).</summary>
public static class FolderRowFormatting
{
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.#} MB";
    }

    public static string FormatImageCount(int count) => count.ToString();

    public static string FormatModified(DateTimeOffset? utc) =>
        utc == null ? "—" : utc.Value.ToLocalTime().ToString("g");
}
