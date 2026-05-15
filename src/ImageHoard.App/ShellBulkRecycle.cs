using System.Runtime.InteropServices;

namespace ImageHoard.App;

/// <summary>Shell <see cref="IFileOperation"/> vtable (shobjidl_core) for batch recycle (FR-SR-08).</summary>
[ComImport]
[Guid("947AAB1F-0A46-4C94-AA14-174ED38D233C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOperationCom
{
    [PreserveSig]
    int SetOperationFlags(uint operationFlags);

    [PreserveSig]
    int SetProgressMessage(
        [MarshalAs(UnmanagedType.LPWStr)] string? pszTitle,
        [MarshalAs(UnmanagedType.LPWStr)] string? pszText);

    [PreserveSig]
    int SetProgressDialog(IntPtr popd);

    [PreserveSig]
    int SetProperties(IntPtr punk);

    [PreserveSig]
    int SetOwnerWindow(IntPtr hwnd);

    [PreserveSig]
    int ApplyPropertiesToItem(IntPtr psiSource, IntPtr pfops);

    [PreserveSig]
    int RenameItem(
        IntPtr psiDestination,
        [MarshalAs(UnmanagedType.LPWStr)] string pszNewName,
        IntPtr pfops);

    [PreserveSig]
    int RenameItems(uint unpszItem, IntPtr psiItemArray, IntPtr ppszNewNames);

    [PreserveSig]
    int MoveItem(
        IntPtr psiItem,
        IntPtr psiDestinationFolder,
        [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName,
        IntPtr pfops);

    [PreserveSig]
    int MoveItems(IntPtr psiItems, IntPtr psiDestinationFolder);

    [PreserveSig]
    int CopyItem(
        IntPtr psiItem,
        IntPtr psiDestinationFolder,
        [MarshalAs(UnmanagedType.LPWStr)] string? pszCopyName,
        IntPtr pfops);

    [PreserveSig]
    int CopyItems(IntPtr psiItems, IntPtr psiDestinationFolder);

    [PreserveSig]
    int DeleteItem(IntPtr psiItem, IntPtr pfops);

    [PreserveSig]
    int DeleteItems(IntPtr psiItemArray);

    [PreserveSig]
    int NewItem(
        IntPtr psiDestinationFolder,
        uint dwFileAttributes,
        [MarshalAs(UnmanagedType.LPWStr)] string pszName,
        [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName,
        IntPtr pfops);

    [PreserveSig]
    int PerformOperations();

    [PreserveSig]
    int GetAnyOperationsAborted(out int pfAnyOperationsAborted);
}

/// <summary>FR-SR-08 — batch send files to Recycle Bin via one <see cref="IFileOperation"/>.</summary>
internal static class ShellBulkRecycle
{
    private static readonly Guid ClsidFileOperation = new("3AD05575-8857-4C00-9271-31BCAA11C414");
    private static readonly Guid IidIFileOperation = typeof(IFileOperationCom).GUID;
    private static readonly Guid IidIShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    private const uint ClsctxAll = 23;

    // FOFX_RECYCLE_IF_POSSIBLE | FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI
    private const uint RecycleSilentFlags = 0x00020000u | 0x00000004u | 0x00000010u | 0x00000400u;

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(
        ref Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IFileOperationCom ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        ref Guid riid,
        out IntPtr ppv);

    /// <summary>
    /// Runs one shell delete-to-recycle operation for all paths that exist. Must be called from the UI STA thread.
    /// After <see langword="true"/>, callers should treat paths no longer on disk as recycled; paths still on disk failed.
    /// </summary>
    public static bool TryPerformBatchRecycleToBin(IReadOnlyList<string> absoluteFilePaths)
    {
        if (absoluteFilePaths.Count == 0)
            return true;

        var clsid = ClsidFileOperation;
        var iidOp = IidIFileOperation;
        var hr = CoCreateInstance(
            ref clsid,
            IntPtr.Zero,
            ClsctxAll,
            ref iidOp,
            out var op);
        if (hr != 0 || op == null)
            return false;

        try
        {
            hr = op.SetOperationFlags(RecycleSilentFlags);
            if (hr != 0)
                return false;

            foreach (var path in absoluteFilePaths)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    continue;

                var iidShell = IidIShellItem;
                hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iidShell, out var psi);
                if (hr != 0 || psi == IntPtr.Zero)
                    return false;

                try
                {
                    hr = op.DeleteItem(psi, IntPtr.Zero);
                    if (hr != 0)
                        return false;
                }
                finally
                {
                    _ = Marshal.Release(psi);
                }
            }

            hr = op.PerformOperations();
            if (hr != 0)
                return false;

            op.GetAnyOperationsAborted(out var aborted);
            return aborted == 0;
        }
        finally
        {
            if (op != null)
                Marshal.ReleaseComObject(op);
        }
    }
}
