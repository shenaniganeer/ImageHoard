using System.IO;

namespace ImageHoard.App;

/// <summary>Single-item rename with Windows-style auto-suffix before extension on collision.</summary>
internal static class BrowserPaneRenameHelper
{
    public static string PickUniqueFileName(string directory, string desiredName)
    {
        var name = desiredName.TrimEnd();
        if (string.IsNullOrEmpty(name))
            throw new InvalidOperationException("Name is empty.");

        var ext = Path.GetExtension(name);
        var baseName = Path.GetFileNameWithoutExtension(name);
        var candidate = Path.Combine(directory, name);
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
            return candidate;

        for (var i = 2; i < 10_000; i++)
        {
            var stem = $"{baseName} ({i})";
            var fn = string.IsNullOrEmpty(ext) ? stem : stem + ext;
            candidate = Path.Combine(directory, fn);
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }

        throw new IOException("Could not find a free name after many attempts.");
    }

    public static string PickUniqueDirectoryName(string parentDirectory, string desiredFolderName)
    {
        var name = desiredFolderName.TrimEnd();
        if (string.IsNullOrEmpty(name))
            throw new InvalidOperationException("Name is empty.");

        var candidate = Path.Combine(parentDirectory, name);
        if (!Directory.Exists(candidate) && !File.Exists(candidate))
            return candidate;

        for (var i = 2; i < 10_000; i++)
        {
            var fn = $"{name} ({i})";
            candidate = Path.Combine(parentDirectory, fn);
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
                return candidate;
        }

        throw new IOException("Could not find a free folder name after many attempts.");
    }
}
