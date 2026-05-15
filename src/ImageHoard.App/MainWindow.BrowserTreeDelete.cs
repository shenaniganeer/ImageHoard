using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageHoard.Core.Browse;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ImageHoard.App;

public sealed partial class MainWindow
{
    internal async void QueueExecuteBrowserTreeDeleteFromKeyboardAsync()
    {
        try
        {
            await ExecuteBrowserTreeDeleteForCurrentSelectionAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetTransientStatus("Delete failed: " + ex.Message);
        }
    }

    internal async void BrowserContextDelete_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_browserContextMenuIsToolbarCurrentFolder)
            {
                _browserContextMenuIsToolbarCurrentFolder = false;
                await ExecuteBrowserTreeDeleteToolbarCurrentFolderAsync().ConfigureAwait(true);
                return;
            }

            if (_browserContextMenuTargetNode != null)
                SyncBrowseTreeSelection(_browserContextMenuTargetNode);

            await ExecuteBrowserTreeDeleteForCurrentSelectionAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetTransientStatus("Delete failed: " + ex.Message);
        }
    }

    private async Task ExecuteBrowserTreeDeleteToolbarCurrentFolderAsync()
    {
        var work = _currentFolderPath;
        if (string.IsNullOrEmpty(work) || !Directory.Exists(work))
            return;
        if (!TreeDeletePathsAreUnderBrowseRoot(new[] { work }))
        {
            SetTransientStatus("Folder is outside the browse tree.");
            return;
        }

        if (!await TryConfirmSendFolderPathToRecycleBinDialogAsync(work).ConfigureAwait(true))
            return;
        await ExecuteSendFolderToRecycleBinAfterConfirmAsync(work).ConfigureAwait(true);
    }

    private bool TreeDeletePathsAreUnderBrowseRoot(IReadOnlyList<string> paths)
    {
        var root = _currentFolderPath;
        if (string.IsNullOrEmpty(root))
            return false;
        foreach (var p in paths)
        {
            if (string.IsNullOrEmpty(p))
                return false;
            if (!IsSameOrDescendantDirectory(root, p))
                return false;
        }

        return true;
    }

    private async Task ExecuteBrowserTreeDeleteForCurrentSelectionAsync()
    {
        if (IsBrowserPaneMutationInProgress || _renameTargetNode != null)
            return;

        var browseRoot = _currentFolderPath;
        if (string.IsNullOrEmpty(browseRoot))
        {
            SetTransientStatus("Nothing to delete.");
            return;
        }

        var nodes = GetSelectedBrowserTreeNavNodes();
        if (nodes.Count == 0)
        {
            SetTransientStatus("Nothing to delete.");
            return;
        }

        var filePaths = new List<string>();
        var folderPaths = new List<string>();
        foreach (var n in nodes)
        {
            switch (n.Content)
            {
                case ImageRow row:
                    filePaths.Add(row.FullPath);
                    break;
                case FolderTreeEntry fe:
                    folderPaths.Add(fe.Path);
                    break;
            }
        }

        var allProbe = filePaths.Concat(folderPaths).ToList();
        if (!TreeDeletePathsAreUnderBrowseRoot(allProbe))
        {
            SetTransientStatus("Selection is outside the browse folder.");
            return;
        }

        var (files, folders) = BrowserTreeDeletePathDedupe.BuildDeletionPathLists(filePaths, folderPaths);
        var total = files.Count + folders.Count;
        if (total == 0)
            return;

        if (total == 1 && files.Count == 1)
        {
            var fp = files[0];
            if (!File.Exists(fp))
            {
                SetTransientStatus("File no longer exists.");
                return;
            }

            var parent = Path.GetDirectoryName(fp) ?? string.Empty;
            if (WizardImageDeletionPreflight.SuggestsPermanentMayBeNeeded(parent))
            {
                var preDlg = new ContentDialog
                {
                    Title = "Permanent delete may be required",
                    Content = new TextBlock
                    {
                        Text =
                            "This folder appears to be on a network location where the Recycle Bin is often unavailable. " +
                            "If sending to the Recycle Bin fails, ImageHoard can permanently delete those files instead. " +
                            "Permanent deletion cannot be undone from the app.\n\n" +
                            "Files in this operation: 1.",
                        TextWrapping = TextWrapping.WrapWholeWords,
                    },
                    PrimaryButtonText = "Continue",
                    CloseButtonText = "Cancel",
                    XamlRoot = RootGrid.XamlRoot,
                };
                if (await ShowWizardContentDialogAsync(preDlg).ConfigureAwait(true) != ContentDialogResult.Primary)
                    return;
            }

            var dlg = new ContentDialog
            {
                Title = "Delete file?",
                Content = new TextBlock
                {
                    Text = $"Send this file to the Recycle Bin?\n\n{Path.GetFileName(fp)}",
                    TextWrapping = TextWrapping.WrapWholeWords,
                },
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot,
            };
            if (await ShowWizardContentDialogAsync(dlg).ConfigureAwait(true) != ContentDialogResult.Primary)
                return;

            if (!await WizardExecuteImageRecycleOrPermanentBatchAsync(
                    new[] { fp },
                    recordUndoForRecycledPaths: false,
                    "BrowserTreeDelete",
                    browseRoot).ConfigureAwait(true))
                return;
        }
        else if (total == 1 && folders.Count == 1)
        {
            var fd = folders[0];
            if (!Directory.Exists(fd))
            {
                SetTransientStatus("Folder no longer exists.");
                return;
            }

            if (!await TryConfirmSendFolderPathToRecycleBinDialogAsync(fd).ConfigureAwait(true))
                return;
            if (!await ExecuteSendFolderToRecycleBinAfterConfirmAsync(fd).ConfigureAwait(true))
                return;
        }
        else
        {
            var summary = new TextBlock
            {
                Text =
                    $"Send {total} items to the Recycle Bin?\n\n" +
                    $"{files.Count} file(s), {folders.Count} folder(s). " +
                    "Folders include all files and subfolders.",
                TextWrapping = TextWrapping.WrapWholeWords,
            };
            var batchDlg = new ContentDialog
            {
                Title = "Delete items?",
                Content = summary,
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot,
            };
            if (await ShowWizardContentDialogAsync(batchDlg).ConfigureAwait(true) != ContentDialogResult.Primary)
                return;

            if (WizardImageDeletionPreflight.SuggestsPermanentMayBeNeeded(browseRoot) && files.Count > 0)
            {
                var preDlg = new ContentDialog
                {
                    Title = "Permanent delete may be required",
                    Content = new TextBlock
                    {
                        Text =
                            "This folder appears to be on a network location where the Recycle Bin is often unavailable. " +
                            "If sending to the Recycle Bin fails, ImageHoard can permanently delete those files instead. " +
                            "Permanent deletion cannot be undone from the app.\n\n" +
                            $"Files in this operation: {files.Count}.",
                        TextWrapping = TextWrapping.WrapWholeWords,
                    },
                    PrimaryButtonText = "Continue",
                    CloseButtonText = "Cancel",
                    XamlRoot = RootGrid.XamlRoot,
                };
                if (await ShowWizardContentDialogAsync(preDlg).ConfigureAwait(true) != ContentDialogResult.Primary)
                    return;
            }

            if (files.Count > 0)
            {
                if (!await WizardExecuteImageRecycleOrPermanentBatchAsync(
                        files,
                        recordUndoForRecycledPaths: false,
                        "BrowserTreeDelete",
                        browseRoot).ConfigureAwait(true))
                    return;
            }

            foreach (var fd in folders)
            {
                if (!Directory.Exists(fd))
                    continue;
                if (!await ExecuteSendFolderToRecycleBinAfterConfirmAsync(fd).ConfigureAwait(true))
                    return;
            }
        }

        await ClearBrowserTreeSelectionAfterDeleteAsync().ConfigureAwait(true);
    }

    private async Task ClearBrowserTreeSelectionAfterDeleteAsync()
    {
        FolderTree.SelectedNode = null;
        await ApplyBrowserTreeLeadPreviewAsync().ConfigureAwait(true);
    }
}
