using Microsoft.VisualBasic.FileIO;

namespace ImageHoard.App;

/// <summary>FR-SR-08 — Windows Recycle Bin (App-layer; Core stays portable).</summary>
internal static class ShellRecycle
{
    public static void SendFileToRecycleBin(string absolutePath)
    {
        FileSystem.DeleteFile(absolutePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }
}
