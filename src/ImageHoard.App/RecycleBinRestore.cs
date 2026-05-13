using System.IO;

namespace ImageHoard.App;

/// <summary>Restore files from the Recycle Bin by original full path (Shell.Application).</summary>
internal static class RecycleBinRestore
{
    private const int CsidlBitbucket = 10;

    public static bool TryRestoreOriginalPath(string originalFullPath)
    {
        if (string.IsNullOrWhiteSpace(originalFullPath))
            return false;

        string normalized;
        try
        {
            normalized = Path.GetFullPath(originalFullPath);
        }
        catch
        {
            normalized = originalFullPath.Trim();
        }

        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType == null)
            return false;

        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic recycle = shell.NameSpace(CsidlBitbucket);
        if (recycle == null)
            return false;

        foreach (dynamic item in recycle.Items())
        {
            string? orig;
            try
            {
                orig = item.ExtendedProperty("System.Recycle.OriginalLocation");
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrEmpty(orig) || !PathsEqual(orig, normalized))
                continue;

            try
            {
                dynamic verbs = item.Verbs();
                int count = verbs.Count;
                for (int i = 0; i < count; i++)
                {
                    dynamic verb = verbs.Item(i);
                    string name = verb.Name ?? string.Empty;
                    if (name.Contains("Restore", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Undelete", StringComparison.OrdinalIgnoreCase))
                    {
                        verb.DoIt();
                        return true;
                    }
                }
            }
            catch
            {
                // continue scanning
            }
        }

        return false;
    }

    private static bool PathsEqual(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
