using Microsoft.VisualBasic.FileIO;

namespace ImageHoard.App;

/// <summary>FR-SR-08 — Windows Recycle Bin (App-layer; Core stays portable).</summary>
internal static class ShellRecycle
{
    public static void SendFileToRecycleBin(string absolutePath)
    {
        FileSystem.DeleteFile(absolutePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }

    /// <summary>Returns true if the file was sent to the Recycle Bin (file no longer at original path).</summary>
    public static bool TrySendFileToRecycleBin(string absolutePath)
    {
        try
        {
            SendFileToRecycleBin(absolutePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void DeleteFilePermanently(string absolutePath)
    {
        FileSystem.DeleteFile(absolutePath, UIOption.OnlyErrorDialogs, RecycleOption.DeletePermanently);
    }

    public static void SendDirectoryToRecycleBin(string absolutePath)
    {
        FileSystem.DeleteDirectory(
            absolutePath,
            UIOption.OnlyErrorDialogs,
            RecycleOption.SendToRecycleBin,
            UICancelOption.DoNothing);
    }
}
