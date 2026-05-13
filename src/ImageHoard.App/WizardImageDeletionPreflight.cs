using System.IO;

namespace ImageHoard.App;

internal static class WizardImageDeletionPreflight
{
    /// <summary>True when the working folder is likely to skip the Recycle Bin (UNC / mapped network drive).</summary>
    public static bool SuggestsPermanentMayBeNeeded(string workingFolderPath)
    {
        if (string.IsNullOrEmpty(workingFolderPath))
            return false;
        if (workingFolderPath.StartsWith(@"\\", StringComparison.Ordinal))
            return true;

        try
        {
            var full = Path.GetFullPath(workingFolderPath);
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root))
                return false;
            var di = new DriveInfo(root);
            return di.DriveType == DriveType.Network;
        }
        catch
        {
            return false;
        }
    }
}
