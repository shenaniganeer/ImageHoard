namespace ImageHoard.App;

internal static class DisplayFormat
{
    public static string ByteSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.#} MB";
    }

    public static string FolderModified(DateTimeOffset? utc) =>
        utc == null ? "—" : utc.Value.ToLocalTime().ToString("g");
}
