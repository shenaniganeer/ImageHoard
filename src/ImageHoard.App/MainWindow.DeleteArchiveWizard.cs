using System.IO;
using System.Linq;
using ImageHoard.Core.Browse;
using ImageHoard.Core.Logging;
using ImageHoard.Core.Metrics;
using ImageHoard.Core.Services;
using ImageHoard.Core.Sort;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ImageHoard.App;

public sealed partial class MainWindow
{
    /// <summary>While the delete/archive wizard is open, remembers the working directory so actions still work if the preview path is stale or the image file no longer exists.</summary>
    private string? _deleteArchiveWizardCapturedWorkingFolder;

    private readonly List<string> _wizardSessionUndoRecycledPaths = new();
    private bool _wizardSessionHadPermanentImageDeletes;
    private bool _wizardUndoRunning;
    internal bool WizardImageUndoInProgress => _wizardUndoRunning;
    internal bool _skipNavigateAfterFolderCommit;

    internal bool SessionInverseKeepDeleteBeforeArchiveMove =>
        _session.InverseKeepDeleteBeforeArchiveMove;

    internal void SetInverseKeepDeleteBeforeArchiveMove(bool value)
    {
        _session.InverseKeepDeleteBeforeArchiveMove = value;
        PersistLayout();
    }

    internal void NotifyDeleteArchiveWizardClosed()
    {
        ClearWizardImageUndoSession();
        _deleteArchiveWizardCapturedWorkingFolder = null;
        DeleteArchiveWizardOverlayRoot.Visibility = Visibility.Collapsed;
    }

    private void ClearWizardImageUndoSession()
    {
        _wizardSessionUndoRecycledPaths.Clear();
        _wizardSessionHadPermanentImageDeletes = false;
    }

    internal bool HasWizardImageUndoPending => _wizardSessionUndoRecycledPaths.Count > 0;

    internal bool WizardImagePermanentDeleteNoticeVisible => _wizardSessionHadPermanentImageDeletes;

    internal void DismissWizardPermanentDeleteNotice() => _wizardSessionHadPermanentImageDeletes = false;

    internal sealed record DeleteArchiveWizardCountSnap(
        int Keep,
        int Delete,
        int Unset,
        int NotKeepCount,
        int DeleteFlaggedCount,
        int TotalImages);

    internal bool HasArchiveRootConfigured => !string.IsNullOrEmpty(_session.ArchiveRoot);

    internal bool HasResolvedDeleteArchiveWizardWorkingFolder() =>
        !string.IsNullOrEmpty(TryGetDeleteArchiveWizardWorkingFolder());

    private static string FormatWizardByteSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.#} MB";
    }

    private static async Task<bool> HasImmediateSubdirectoryAsync(
        IFileSystem fileSystem,
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entries = await fileSystem.ListDirectoryAsync(directoryPath, cancellationToken).ConfigureAwait(true);
            return entries.Any(e => e.IsDirectory);
        }
        catch
        {
            return false;
        }
    }

    private async Task<(int FileCount, long SizeBytes)?> TryGetWizardSubtreeMetricsAsync(string directoryPath)
    {
        try
        {
            var snap = await FolderMetricsScanner.ScanSubtreeAsync(AppServices.FileSystem, directoryPath)
                .ConfigureAwait(true);
            return (snap.TotalFileCount, snap.AggregateSizeBytes);
        }
        catch
        {
            return null;
        }
    }

    private string? TryResolveDeleteArchiveWizardDirectoryFromCurrentImage()
    {
        if (string.IsNullOrEmpty(_currentImageFullPath) || !File.Exists(_currentImageFullPath))
            return null;
        var dir = Path.GetDirectoryName(_currentImageFullPath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return null;
        try
        {
            return Path.GetFullPath(dir);
        }
        catch
        {
            return dir;
        }
    }

    private string? TryGetDeleteArchiveWizardWorkingFolder()
    {
        var fromImage = TryResolveDeleteArchiveWizardDirectoryFromCurrentImage();
        if (!string.IsNullOrEmpty(fromImage))
        {
            if (IsDeleteArchiveWizardOverlayOpen)
                _deleteArchiveWizardCapturedWorkingFolder = fromImage;
            return fromImage;
        }

        if (IsDeleteArchiveWizardOverlayOpen
            && !string.IsNullOrEmpty(_deleteArchiveWizardCapturedWorkingFolder)
            && Directory.Exists(_deleteArchiveWizardCapturedWorkingFolder))
            return _deleteArchiveWizardCapturedWorkingFolder;

        return null;
    }

    internal async Task<DeleteArchiveWizardCountSnap?> GetDeleteArchiveWizardCountsAsync()
    {
        var work = TryGetDeleteArchiveWizardWorkingFolder();
        if (string.IsNullOrEmpty(work))
            return null;
        List<string> paths;
        try
        {
            paths = await FolderImagePathCollection.CollectAsync(
                AppServices.FileSystem,
                work,
                includeSubfolders: false).ConfigureAwait(true);
        }
        catch
        {
            return null;
        }

        var (keep, delete, unset) = _sortSession.CountStates(paths);
        var notKeep = paths.Count(p => _sortSession.GetState(p) != SortFlagState.Keep);
        var delFlag = BatchDeletePlanner.GetDeleteFlaggedPaths(paths, _sortSession).Count;
        return new DeleteArchiveWizardCountSnap(keep, delete, unset, notKeep, delFlag, paths.Count);
    }

    internal void ShowOrActivateDeleteArchiveWizard()
    {
        HidePreferencesOverlay();
        var work = TryGetDeleteArchiveWizardWorkingFolder();
        if (string.IsNullOrEmpty(work))
        {
            SetTransientStatus("Select an image in this folder first.");
            return;
        }

        _deleteArchiveWizardCapturedWorkingFolder = work;

        if (IsDeleteArchiveWizardOverlayOpen)
        {
            DeleteArchiveWizardPanelElement.SetFolderPathDisplay(work);
            DeleteArchiveWizardPanelElement.OnOverlayShown();
            DeleteArchiveWizardOverlayRoot.Focus(FocusState.Programmatic);
            return;
        }

        DeleteArchiveWizardOverlayRoot.Visibility = Visibility.Visible;
        DeleteArchiveWizardPanelElement.SetFolderPathDisplay(work);
        DeleteArchiveWizardPanelElement.OnOverlayShown();
        DeleteArchiveWizardOverlayRoot.Focus(FocusState.Programmatic);
    }

    internal async Task WizardExecuteInverseKeepDeleteAsync()
    {
        var work = TryGetDeleteArchiveWizardWorkingFolder();
        if (string.IsNullOrEmpty(work))
        {
            const string msg =
                "Could not resolve the working folder. Select an image in the folder, or close and reopen the wizard.";
            SetTransientStatus(msg);
            ActiveDeleteArchiveWizardPanel?.ShowWizardOperationInfo("Working folder", msg, InfoBarSeverity.Warning);
            return;
        }
        List<string> paths;
        try
        {
            paths = await FolderImagePathCollection.CollectAsync(
                AppServices.FileSystem,
                work,
                includeSubfolders: false).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetTransientStatus("Could not list images: " + ex.Message);
            return;
        }

        if (paths.Count == 0)
        {
            SetTransientStatus("No images found in this folder.");
            ActiveDeleteArchiveWizardPanel?.ShowWizardOperationInfo(
                "Nothing to delete",
                "This folder has no supported image files.",
                InfoBarSeverity.Informational);
            return;
        }

        var toDelete = BatchDeletePlanner.GetInverseKeepDeletionSetIgnoringUnsetGate(paths, _sortSession);
        if (toDelete.Count == 0)
        {
            SetTransientStatus("Nothing to delete (all images are marked Keep).");
            return;
        }

        var confirmDlg = new ContentDialog
        {
            Title = "Delete non-keepers?",
            Content = new TextBlock
            {
                Text =
                    $"Send {toDelete.Count} image(s) not marked Keep to the Recycle Bin (or permanently delete if the Recycle Bin is unavailable)?",
                TextWrapping = TextWrapping.WrapWholeWords,
            },
            PrimaryButtonText = $"Delete {toDelete.Count} image(s)",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot,
        };
        if (await confirmDlg.ShowAsync() != ContentDialogResult.Primary)
            return;

        await WizardExecuteImageRecycleOrPermanentBatchAsync(toDelete, recordUndoForRecycledPaths: true, "BatchDelete")
            .ConfigureAwait(true);
    }

    internal async Task WizardExecuteDeleteFlaggedOnlyAsync()
    {
        var work = TryGetDeleteArchiveWizardWorkingFolder();
        if (string.IsNullOrEmpty(work))
        {
            const string msg =
                "Could not resolve the working folder. Select an image in the folder, or close and reopen the wizard.";
            SetTransientStatus(msg);
            ActiveDeleteArchiveWizardPanel?.ShowWizardOperationInfo("Working folder", msg, InfoBarSeverity.Warning);
            return;
        }
        List<string> paths;
        try
        {
            paths = await FolderImagePathCollection.CollectAsync(
                AppServices.FileSystem,
                work,
                includeSubfolders: false).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetTransientStatus("Could not list images: " + ex.Message);
            return;
        }

        var toDelete = BatchDeletePlanner.GetDeleteFlaggedPaths(paths, _sortSession);
        if (toDelete.Count == 0)
        {
            SetTransientStatus("No images marked Delete in this folder.");
            return;
        }

        var confirmDlg = new ContentDialog
        {
            Title = "Delete marked images?",
            Content = new TextBlock
            {
                Text =
                    $"Send {toDelete.Count} image(s) marked Delete to the Recycle Bin (or permanently delete if the Recycle Bin is unavailable)?",
                TextWrapping = TextWrapping.WrapWholeWords,
            },
            PrimaryButtonText = $"Delete {toDelete.Count} image(s)",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot,
        };
        if (await confirmDlg.ShowAsync() != ContentDialogResult.Primary)
            return;

        await WizardExecuteImageRecycleOrPermanentBatchAsync(
                toDelete,
                recordUndoForRecycledPaths: true,
                "BatchDeleteDeleteFlaggedOnly")
            .ConfigureAwait(true);
    }

    internal async Task<bool> WizardExecuteMoveWorkingFolderToArchiveAsync()
    {
        var work = TryGetDeleteArchiveWizardWorkingFolder();
        if (string.IsNullOrEmpty(work))
        {
            const string msg =
                "Could not resolve the folder to move. Select an image in the folder, or close and reopen the wizard.";
            SetTransientStatus(msg);
            ActiveDeleteArchiveWizardPanel?.ShowWizardOperationInfo("Move to archive", msg, InfoBarSeverity.Warning);
            return false;
        }

        if (string.IsNullOrEmpty(_session.ArchiveRoot))
        {
            const string msg = "Set an archive target folder (browser pane footer or Preferences → Library).";
            SetTransientStatus(msg);
            ActiveDeleteArchiveWizardPanel?.ShowWizardOperationInfo("Move to archive", msg, InfoBarSeverity.Warning);
            return false;
        }

        var name = new DirectoryInfo(work).Name;
        var dest = Path.Combine(_session.ArchiveRoot!, name);

        IReadOnlyList<string> toDeleteBeforeMove = Array.Empty<string>();
        if (_session.InverseKeepDeleteBeforeArchiveMove)
        {
            List<string> paths;
            try
            {
                paths = await FolderImagePathCollection.CollectAsync(
                    AppServices.FileSystem,
                    work,
                    includeSubfolders: false).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                SetTransientStatus("Could not list images: " + ex.Message);
                return false;
            }

            if (paths.Count > 0)
                toDeleteBeforeMove = BatchDeletePlanner.GetInverseKeepDeletionSetIgnoringUnsetGate(paths, _sortSession);
        }

        var hasSubfolders = await HasImmediateSubdirectoryAsync(AppServices.FileSystem, work).ConfigureAwait(true);
        string? subtreeLine = null;
        if (hasSubfolders)
        {
            var metrics = await TryGetWizardSubtreeMetricsAsync(work).ConfigureAwait(true);
            subtreeLine = metrics.HasValue
                ? $"This folder contains subfolders. Entire tree: {metrics.Value.FileCount} files, {FormatWizardByteSize(metrics.Value.SizeBytes)}."
                : "This folder contains subfolders. Could not measure the full folder tree (file count and size unavailable).";
        }

        var moveBodyParts = new List<string>
        {
            $"Move this folder into the archive?\n\nFrom:\n{work}\n\nTo:\n{dest}",
        };
        if (subtreeLine != null)
            moveBodyParts.Add(subtreeLine);
        if (_session.InverseKeepDeleteBeforeArchiveMove && toDeleteBeforeMove.Count > 0)
        {
            moveBodyParts.Add(
                $"First, {toDeleteBeforeMove.Count} image(s) not marked Keep will be deleted (Recycle Bin or permanent delete per follow-up prompts).");
        }

        var moveDlg = new ContentDialog
        {
            Title = "Move to archive?",
            Content = new TextBlock
            {
                Text = string.Join("\n\n", moveBodyParts),
                TextWrapping = TextWrapping.WrapWholeWords,
            },
            PrimaryButtonText = "Move to archive",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot,
        };
        if (await moveDlg.ShowAsync() != ContentDialogResult.Primary)
            return false;

        if (_session.InverseKeepDeleteBeforeArchiveMove && toDeleteBeforeMove.Count > 0)
        {
            if (!await WizardExecuteImageRecycleOrPermanentBatchAsync(
                    toDeleteBeforeMove,
                    recordUndoForRecycledPaths: true,
                    "BatchDelete").ConfigureAwait(true))
                return false;
        }

        try
        {
            await AppServices.FileSystem.MoveDirectoryAsync(work, dest).ConfigureAwait(true);
            if (_session.LogDestructiveOperations)
            {
                var rec = new OperationLogBatchRecord
                {
                    Operation = "MoveToArchive",
                    Summary = new OperationLogSummary { Ok = 1, Failed = 0, Skipped = 0 },
                    Entries =
                    {
                        new OperationLogEntry { Path = work, Result = "Ok", Detail = dest },
                    },
                };
                await OperationLogWriter.AppendAsync(AppDataPaths.OperationsLogPath, rec).ConfigureAwait(true);
            }

            SetTransientStatus("Folder moved to archive.");
            _deleteArchiveWizardCapturedWorkingFolder = null;
            if (string.Equals(_currentFolderPath, work, StringComparison.OrdinalIgnoreCase))
            {
                FolderTree.RootNodes.Clear();
                _browseNavAnchorPath = null;
                _currentFolderPath = null;
                UpdateBrowserToolbar();
                _session.LastBrowseFolder = null;
                _session.LastSelectedImage = null;
                PersistLayout();
            }
            else if (!string.IsNullOrEmpty(_currentFolderPath))
            {
                await NavigateToFolderAsync(_currentFolderPath).ConfigureAwait(true);
            }

            return true;
        }
        catch (Exception ex)
        {
            var detail = "Move failed: " + ex.Message;
            SetTransientStatus(detail);
            ActiveDeleteArchiveWizardPanel?.ShowWizardOperationInfo("Move to archive", detail, InfoBarSeverity.Error);
            return false;
        }
    }

    internal async Task WizardExecuteDeleteWorkingFolderToRecycleAsync()
    {
        var work = TryGetDeleteArchiveWizardWorkingFolder();
        if (string.IsNullOrEmpty(work))
        {
            const string msg =
                "Could not resolve the working folder. Select an image in the folder, or close and reopen the wizard.";
            SetTransientStatus(msg);
            ActiveDeleteArchiveWizardPanel?.ShowWizardOperationInfo("Working folder", msg, InfoBarSeverity.Warning);
            return;
        }

        var deleteFolderParts = new List<string> { "Send this folder and its contents to the Recycle Bin?", work };
        var hasSubfolders = await HasImmediateSubdirectoryAsync(AppServices.FileSystem, work).ConfigureAwait(true);
        if (hasSubfolders)
        {
            var metrics = await TryGetWizardSubtreeMetricsAsync(work).ConfigureAwait(true);
            deleteFolderParts.Add(
                metrics.HasValue
                    ? $"Entire tree: {metrics.Value.FileCount} files, {FormatWizardByteSize(metrics.Value.SizeBytes)}."
                    : "Could not measure the full folder tree (file count and size unavailable).");
        }

        var dlg = new ContentDialog
        {
            Title = "Delete folder?",
            Content = new TextBlock
            {
                Text = string.Join("\n\n", deleteFolderParts),
                TextWrapping = TextWrapping.WrapWholeWords,
            },
            PrimaryButtonText = "Delete folder",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary)
            return;

        try
        {
            ShellRecycle.SendDirectoryToRecycleBin(work);
            if (_session.LogDestructiveOperations)
            {
                var rec = new OperationLogBatchRecord
                {
                    Operation = "DeleteFolderRecycle",
                    Summary = new OperationLogSummary { Ok = 1, Failed = 0, Skipped = 0 },
                    Entries = { new OperationLogEntry { Path = work, Result = "Ok" } },
                };
                try
                {
                    await OperationLogWriter.AppendAsync(AppDataPaths.OperationsLogPath, rec).ConfigureAwait(true);
                }
                catch
                {
                    // ignored
                }
            }

            SetTransientStatus("Folder sent to the Recycle Bin.");
            _deleteArchiveWizardCapturedWorkingFolder = null;
            if (string.Equals(_currentFolderPath, work, StringComparison.OrdinalIgnoreCase))
            {
                FolderTree.RootNodes.Clear();
                _browseNavAnchorPath = null;
                _currentFolderPath = null;
                UpdateBrowserToolbar();
                _session.LastBrowseFolder = null;
                _session.LastSelectedImage = null;
                PersistLayout();
            }
            else if (!string.IsNullOrEmpty(_currentFolderPath))
            {
                await NavigateToFolderAsync(_currentFolderPath).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            SetTransientStatus("Delete folder failed: " + ex.Message);
        }
    }

    /// <returns><see langword="false"/> if the user cancelled the permanent-delete preflight; otherwise <see langword="true"/>.</returns>
    private async Task<bool> WizardExecuteImageRecycleOrPermanentBatchAsync(
        IReadOnlyList<string> toDelete,
        bool recordUndoForRecycledPaths,
        string operationNameForLog)
    {
        if (toDelete.Count == 0)
        {
            SetTransientStatus("Nothing to delete.");
            return true;
        }

        if (recordUndoForRecycledPaths)
        {
            _wizardSessionUndoRecycledPaths.Clear();
            _wizardSessionHadPermanentImageDeletes = false;
        }

        var work = TryGetDeleteArchiveWizardWorkingFolder() ?? string.Empty;
        var preNeedsPermanentDialog = WizardImageDeletionPreflight.SuggestsPermanentMayBeNeeded(work);

        var userApprovedPermanentForFailures = false;
        if (preNeedsPermanentDialog)
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
                        $"Files in this operation: {toDelete.Count}.",
                    TextWrapping = TextWrapping.WrapWholeWords,
                },
                PrimaryButtonText = "Continue",
                CloseButtonText = "Cancel",
                XamlRoot = RootGrid.XamlRoot,
            };
            if (await preDlg.ShowAsync() != ContentDialogResult.Primary)
                return false;
            userApprovedPermanentForFailures = true;
        }

        var userApprovedMidBatchPermanent = false;
        var entries = new List<OperationLogEntry>();
        var toRecycleBin = 0;
        var permanentDeleted = 0;
        var skippedDeclined = 0;
        var failed = 0;

        foreach (var p in toDelete)
        {
            if (ShellRecycle.TrySendFileToRecycleBin(p))
            {
                toRecycleBin++;
                entries.Add(new OperationLogEntry { Path = p, Result = "Ok" });
                if (recordUndoForRecycledPaths &&
                    !_wizardSessionUndoRecycledPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
                    _wizardSessionUndoRecycledPaths.Add(p);
                continue;
            }

            if (!userApprovedPermanentForFailures && !userApprovedMidBatchPermanent)
            {
                var midDlg = new ContentDialog
                {
                    Title = "Recycle Bin unavailable",
                    Content = new TextBlock
                    {
                        Text =
                            "At least one file could not be sent to the Recycle Bin. " +
                            "Permanently delete files that cannot be recycled for the rest of this operation? " +
                            "Permanent deletion cannot be undone from the app.",
                        TextWrapping = TextWrapping.WrapWholeWords,
                    },
                    PrimaryButtonText = "Permanently delete",
                    CloseButtonText = "Cancel",
                    XamlRoot = RootGrid.XamlRoot,
                };
                if (await midDlg.ShowAsync() != ContentDialogResult.Primary)
                {
                    skippedDeclined++;
                    entries.Add(new OperationLogEntry
                    {
                        Path = p,
                        Result = "Skipped",
                        Detail = "Permanent delete declined",
                    });
                    continue;
                }

                userApprovedMidBatchPermanent = true;
            }

            try
            {
                ShellRecycle.DeleteFilePermanently(p);
                permanentDeleted++;
                entries.Add(new OperationLogEntry { Path = p, Result = "Ok", Detail = "Permanent delete" });
            }
            catch (Exception ex)
            {
                failed++;
                entries.Add(new OperationLogEntry { Path = p, Result = "Failed", Detail = ex.Message });
            }
        }

        if (recordUndoForRecycledPaths && permanentDeleted > 0)
            _wizardSessionHadPermanentImageDeletes = true;

        if (_session.LogDestructiveOperations && entries.Count > 0)
        {
            var rec = new OperationLogBatchRecord
            {
                Operation = operationNameForLog,
                Summary = new OperationLogSummary
                {
                    Ok = toRecycleBin + permanentDeleted,
                    Failed = failed,
                    Skipped = skippedDeclined,
                },
                Entries = entries,
            };
            try
            {
                await OperationLogWriter.AppendAsync(AppDataPaths.OperationsLogPath, rec).ConfigureAwait(true);
            }
            catch
            {
                // ignored
            }
        }

        if (recordUndoForRecycledPaths)
        {
            if (permanentDeleted > 0 && toRecycleBin > 0)
            {
                SetTransientStatus(
                    $"Recycle Bin: {toRecycleBin} file(s). Permanently deleted: {permanentDeleted}.");
            }
            else if (permanentDeleted > 0)
            {
                SetTransientStatus($"Permanently deleted {permanentDeleted} file(s).");
            }
            else if (skippedDeclined > 0 && toRecycleBin == 0)
            {
                SetTransientStatus("No files were deleted (permanent delete was not confirmed).");
            }
            else if (skippedDeclined > 0)
            {
                SetTransientStatus(
                    $"Recycle Bin: {toRecycleBin} file(s). Skipped {skippedDeclined} (permanent delete not confirmed).");
            }
            else
            {
                SetTransientStatus($"Sent {toRecycleBin} image(s) to the Recycle Bin.");
            }
        }

        if (!string.IsNullOrEmpty(_currentFolderPath))
            await NavigateToFolderAsync(_currentFolderPath).ConfigureAwait(true);

        ActiveDeleteArchiveWizardPanel?.RefreshUndoAndNoticeUi();
        return true;
    }

    internal async Task WizardUndoLastImageDeletesAsync()
    {
        if (_wizardUndoRunning || _wizardSessionUndoRecycledPaths.Count == 0)
            return;
        _wizardUndoRunning = true;
        try
        {
            var list = _wizardSessionUndoRecycledPaths.ToList();
            var restored = 0;
            foreach (var p in list)
            {
                if (RecycleBinRestore.TryRestoreOriginalPath(p))
                    restored++;
            }

            _wizardSessionUndoRecycledPaths.Clear();
            SetTransientStatus($"Restored {restored} of {list.Count} file(s) from the Recycle Bin.");
            if (!string.IsNullOrEmpty(_currentFolderPath))
                await NavigateToFolderAsync(_currentFolderPath).ConfigureAwait(true);
        }
        finally
        {
            _wizardUndoRunning = false;
            ActiveDeleteArchiveWizardPanel?.RefreshUndoAndNoticeUi();
        }
    }

    internal void WizardRequestRenameWorkingFolder()
    {
        var work = TryGetDeleteArchiveWizardWorkingFolder();
        if (string.IsNullOrEmpty(work))
        {
            const string msg =
                "Could not resolve the working folder. Select an image in the folder, or close and reopen the wizard.";
            SetTransientStatus(msg);
            ActiveDeleteArchiveWizardPanel?.ShowWizardOperationInfo("Working folder", msg, InfoBarSeverity.Warning);
            return;
        }
        if (!TryBeginRenameFolderByPath(work))
        {
            SetTransientStatus("Expand the tree so the folder is visible, then try again.");
            return;
        }

        _skipNavigateAfterFolderCommit = true;
        Activate();
    }
}