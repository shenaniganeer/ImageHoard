namespace ImageHoard.Core.Browse;

/// <summary>
/// MVP raster extensions (PRD §4.2 baseline). Matching is case-insensitive.
/// </summary>
public static class ImageExtensions
{
    private static readonly HashSet<string> Extensions =
    [
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".heic", ".heif", ".ico"
    ];

    /// <summary>Distinct dotted extensions for <c>FileOpenPicker.FileTypeFilter</c> (e.g. <c>.jpg</c>).</summary>
    public static IReadOnlyList<string> PickerFileTypeExtensions { get; } = Extensions.ToArray();

    public static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path);
        return Extensions.Contains(ext.ToLowerInvariant());
    }
}
