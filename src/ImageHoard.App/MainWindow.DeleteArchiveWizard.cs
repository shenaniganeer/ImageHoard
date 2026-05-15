using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageHoard.Core.Browse;
using ImageHoard.Core.Logging;
using ImageHoard.Core.Metrics;
using ImageHoard.Core.Services;
using ImageHoard.Core.Sort;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ImageHoard.App;

public sealed partial class MainWindow
{
    /// <summary>While the delete/archive wizard is open, remembers the working directory so actions still work if the preview path is stale or the image file no longer exists.</summary>
    private string? _deleteArchiveWizardCapturedWorkingFolder;

    /// <summary>When set (e.g. browser tree "open archive wizard in this folder"), scopes the wizard to this directory ahead of the current image's parent.</summary>
    private string? _deleteArchiveWizardFolderPathOverride;

    private readonly List<string> _wizardSessionUndoRecycledPaths = new();
    private bool _wizardSessionHadPermanentImageDeletes;
    private bool _wizardUndoRunning;
    internal bool WizardImageUndoInProgress => _wizardUndoRunning;

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
        _deleteArchiveWizardFolderPathOverride = null;
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

    internal async Task<bool> DeleteArchiveWizardWorkingFolderHasImmediateSubfoldersAsync()
    {
        var work = TryGetDeleteArchiveWizardWorkingFolder();
        if (string.IsNullOrEmpty(work))
            return false;
        return await HasImmediateSubdirectoryAsync(AppServices.FileSystem, work).ConfigureAwait(true);
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
        if (!string.IsNullOrEmpty(_deleteArchiveWizardFolderPathOverride))
        {
            if (Directory.Exists(_deleteArchiveWizardFolderPathOverride))
            {
                if (IsDeleteArchiveWizardOverlayOpen)
                    _deleteArchiveWizardCapturedWorkingFolder = _deleteArchiveWizardFolderPathOverride;
                return _deleteArchiveWizardFolderPathOverride;
            }

            _deleteArchiveWizardFolderPathOverride = null;
        }

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
        HideBrowserFindOverlay();
        HidePreferencesOverlay();
        _deleteArchiveWizardFolderPathOverride = null;
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

    internal void ShowOrActivateDeleteArchiveWizardForFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return;

        string normalized;
        try
        {
            normalized = Path.GetFullPath(folderPath.Trim());
        }
        catch
        {
            SetTransientStatus("Could not resolve that folder path.");
            return;
        }

        if (!Directory.Exists(normalized))
        {
            SetTransientStatus("Folder does not exist.");
            return;
        }

        if (!TreeDeletePathsAreUnderBrowseRoot(new[] { normalized }))
        {
            SetTransientStatus("Folder is outside the browse tree.");
            return;
        }

        HideBrowserFindOverlay();
        HidePreferencesOverlay();

        _deleteArchiveWizardFolderPathOverride = normalized;
        _deleteArchiveWizardCapturedWorkingFolder = normalized;

        if (IsDeleteArchiveWizardOverlayOpen)
        {
            DeleteArchiveWizardPanelElement.SetFolderPathDisplay(normalized);
            DeleteArchiveWizardPanelElement.OnOverlayShown();
            DeleteArchiveWizardOverlayRoot.Focus(FocusState.Programmatic);
            return;
        }

        DeleteArchiveWizardOverlayRoot.Visibility = Visibility.Visible;
        DeleteArchiveWizardPanelElement.SetFolderPathDisplay(normalized);
        DeleteArchiveWizardPanelElement.OnOverlayShown();
        DeleteArchiveWizardOverlayRoot.Focus(FocusState.Programmatic);
    }

    internal async Task<bool> WizardExecuteInverseKeepDeleteAsync()
    {
        var work = TryGetDeleteArchiveWizardWorkingFolder();
        if (string.IsNullOrEmpty(work))
        {
            const string msg =
                "Could not resolve the working folder. Select an image in the folder, or close and reopen the wizard.";
            SetTransientStatus(msg);
            ActiveDeleteArchiveWizardPanel?.ShowWizardOperationInfo("Working folder", msg, InfoBarSeverity.Warning);
            return false;
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
            return false;
        }

        if (paths.Count == 0)
        {
            SetTransientStatus("No images found in this folder.");
            ActiveDeleteArchiveWizardPanel?.ShowWizardOperationInfo(
                "Nothing to delete",
                "This folder has no supported image files.",
                InfoBarSeverity.Informational);
            return false;
        }

        var toDelete = BatchDeletePlanner.GetInverseKeepDeletionSetIgnoringUnsetGate(paths, _sortSession);
        if (toDelete.Count == 0)
        {
            SetTransientStatus("Nothing to delete (all images are marked Keep).");
            return false;
        }

        var confirmDlg = new ContentDialog
        {
            Title = "Delete non-keepers?",
            Content = new TextBlock
            {
                Text =
                    $"{toDelete.Count} image(s) not marked Keep will be deleted.",
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Crimson),
                TextWrapping = TextWrapping.WrapWholeWords,
            },
            PrimaryButtonText = $"Delete {toDelete.Count} image(s)",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot,
        };
        if (await ShowWizardContentDialogAsync(confirmDlg) != ContentDialogResult.Primary)
            return false;

        return await WizardExecuteImageRecycleOrPermanentBatchAsync(toDelete, recordUndoForRecycledPaths: true, "BatchDelete")
            .ConfigureAwait(true);
    }

    internal async Task<bool> WizardExecuteDeleteFlaggedOnlyAsync()
    {
        var work = TryGetDeleteArchiveWizardWorkingFolder();
        if (string.IsNullOrEmpty(work))
        {
            const string msg =
                "Could not resolve the working folder. Select an image in the folder, or close and reopen the wizard.";
            SetTransientStatus(msg);
            ActiveDeleteArchiveWizardPanel?.ShowWizardOperationInfo("Working folder", msg, InfoBarSeverity.Warning);
            return false;
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
            return false;
        }

        var toDelete = BatchDeletePlanner.GetDeleteFlaggedPaths(paths, _sortSession);
        if (toDelete.Count == 0)
        {
            SetTransientStatus("No images marked Delete in this folder.");
            return false;
        }

        var confirmDlg = new ContentDialog
        {
            Title = "Delete marked images?",
            Content = new TextBlock
            {
                Text =
                    $"Send {toDelete.Count} image(s) marked Delete to the Recycle Bin (or permanently delete if the Recycle Bin is unavailable)?",
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red),
                TextWrapping = TextWrapping.WrapWholeWords,
            },
            PrimaryButtonText = $"Delete {toDelete.Count} image(s)",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot,
        };
        if (await ShowWizardContentDialogAsync(confirmDlg) != ContentDialogResult.Primary)
            return false;

        return await WizardExecuteImageRecycleOrPermanentBatchAsync(
                toDelete,
                recordUndoForRecycledPaths: true,
                "BatchDeleteDeleteFlaggedOnly")
            .ConfigureAwait(true);
    }

    private async Task<string?> TryComputePreferredNextSiblingFolderAsync(string removedFolderFullPath)
    {
        var parent = Directory.GetParent(removedFolderFullPath)?.FullName;
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            return null;
        try
        {
            var entries = await AppServices.FileSystem.ListDirectoryAsync(parent).ConfigureAwait(true);
            var sorted = FolderDirectorySort.SortDirectories(
                entries,
                _layoutState.FolderListSort,
                _folderAggregateBytesByPath,
                _folderImageFileCountByPath);
            var pick = FolderDirectorySort.PickAdjacentSiblingAfterRemoval(sorted, removedFolderFullPath);
            return pick?.FullPath;
        }
        catch
        {
            return null;
        }
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
            const string msg = "Set an archive target folder (browser pane footer or Preferences → Advanced).";
            SetTransientStatus(msg);
            ActiveDeleteArchiveWizardPanel?.ShowWizardOperationInfo("Move to archive", msg, InfoBarSeverity.Warning);
            return false;
        }

        var name = new DirectoryInfo(work).Name;
        var dest = Path.Combine(_session.ArchiveRoot!, name);

        var hasSubfolders = await HasImmediateSubdirectoryAsync(AppServices.FileSystem, work).ConfigureAwait(true);
        if (hasSubfolders)
        {
            const string msg =
                "Move to archive is only available for folders without subfolders. Flatten or reorganize this folder first.";
            SetTransientStatus(msg);
            ActiveDeleteArchiveWizardPanel?.ShowWizardOperationInfo("Move to archive", msg, InfoBarSeverity.Warning);
            return false;
        }

        try
        {
            var preMerge = await GalleryArchiveTargetAnalyzer.AnalyzeAsync(
                    AppServices.FileSystem,
                    _session.ArchiveRoot,
                    work,
                    CancellationToken.None)
                .ConfigureAwait(true);
            if (preMerge.HasContentConflict)
            {
                const string msg =
                    "The archive already has file(s) with the same name(s) but different content. Resolve differences manually, then try again.";
                SetTransientStatus(msg);
                ActiveDeleteArchiveWizardPanel?.ShowWizardOperationInfo("Move to archive", msg, InfoBarSeverity.Warning);
                return false;
            }
        }
        catch (Exception ex)
        {
            SetTransientStatus("Could not compare folder with archive: " + ex.Message);
            ActiveDeleteArchiveWizardPanel?.ShowWizardOperationInfo("Move to archive", ex.Message, InfoBarSeverity.Warning);
            return false;
        }

        List<string> moveFolderImagePaths;
        try
        {
            moveFolderImagePaths = await FolderImagePathCollection.CollectAsync(
                    AppServices.FileSystem,
                    work,
                    includeSubfolders: false)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetTransientStatus("Could not list images: " + ex.Message);
            return false;
        }

        var toDeleteBeforeMoveSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_session.InverseKeepDeleteBeforeArchiveMove && moveFolderImagePaths.Count > 0)
        {
            foreach (var p in BatchDeletePlanner.GetInverseKeepDeletionSetIgnoringUnsetGate(
                         moveFolderImagePaths,
                         _sortSession))
                toDeleteBeforeMoveSet.Add(p);
        }

        foreach (var p in BatchDeletePlanner.GetDeleteFlaggedPaths(moveFolderImagePaths, _sortSession))
            toDeleteBeforeMoveSet.Add(p);

        var toDeleteBeforeMove = toDeleteBeforeMoveSet.ToList();

        var totalImages = moveFolderImagePaths.Count;
        var movedImages = totalImages - toDeleteBeforeMove.Count;

        var greenBrush = new SolidColorBrush(Microsoft.UI.Colors.Green);
        var redBrush = new SolidColorBrush(Microsoft.UI.Colors.Red);
        var moveDialogContent = new StackPanel();
        moveDialogContent.Children.Add(
            new TextBlock
            {
                Text = $"Move this folder into the archive?\n\nFrom:\n{work}\n\nTo:\n{dest}",
                TextWrapping = TextWrapping.WrapWholeWords,
            });

        if (movedImages > 0)
        {
            moveDialogContent.Children.Add(
                new TextBlock
                {
                    Text = $"{movedImages} image(s) will be moved to the archive.",
                    Foreground = greenBrush,
                    TextWrapping = TextWrapping.WrapWholeWords,
                    Margin = new Thickness(0, 12, 0, 0),
                });
        }

        if (toDeleteBeforeMove.Count > 0)
        {
            var warnText = _session.InverseKeepDeleteBeforeArchiveMove
                ? $"{toDeleteBeforeMove.Count} image(s) not marked Keep will be deleted."
                : $"{toDeleteBeforeMove.Count} image(s) marked Delete will be sent to the Recycle Bin " +
                  "(or permanently deleted if the Recycle Bin is unavailable). They will not be moved to the archive.";
            moveDialogContent.Children.Add(
                new TextBlock
                {
                    Text = warnText,
                    Foreground = redBrush,
                    TextWrapping = TextWrapping.WrapWholeWords,
                    Margin = new Thickness(0, 12, 0, 0),
                });
        }

        var moveDlg = new ContentDialog
        {
            Title = "Move to archive?",
            Content = moveDialogContent,
            PrimaryButtonText = "Move to archive",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot,
        };
        if (await ShowWizardContentDialogAsync(moveDlg) != ContentDialogResult.Primary)
            return false;

        if (toDeleteBeforeMove.Count > 0)
        {
            var batchOpName = _session.InverseKeepDeleteBeforeArchiveMove
                ? "BatchDelete"
                : "BatchDeleteDeleteFlaggedOnly";
            if (!await WizardExecuteImageRecycleOrPermanentBatchAsync(
                    toDeleteBeforeMove,
                    recordUndoForRecycledPaths: true,
                    batchOpName,
                    deferBrowserPaneRefresh: true).ConfigureAwait(true))
                return false;
        }

        var preferredNext = await TryComputePreferredNextSiblingFolderAsync(work).ConfigureAwait(true);

        EnterBrowserPaneMutation();
        try
        {
            await AppServices.FileSystem.MergeMoveDirectoryAsync(work, dest).ConfigureAwait(true);
            ClearDeferredWizardBatchBrowserRefreshCapture();
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

            SetTransientStatus("Folder moved to archive.");
            _deleteArchiveWizardCapturedWorkingFolder = null;
            var parent = Directory.GetParent(work)?.FullName;
            if (string.Equals(_currentFolderPath, work, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                {
                    await ReconcileBrowserPaneAfterWizardNavigateToParentAsync(
                            parent,
                            new BrowserTreeRefocusAfterWizardContext(preferredNext))
                        .ConfigureAwait(true);
                }
                else
                {
                    FolderTree.RootNodes.Clear();
                    _browseNavAnchorPath = null;
                    _currentFolderPath = null;
                    UpdateBrowserToolbar();
                    _session.LastBrowseFolder = null;
                    _session.LastSelectedImage = null;
                    _session.BrowserTree = null;
                    ClearImageSelectionAndPreviewCore();
                    PersistLayout();
                }
            }
            else if (!string.IsNullOrEmpty(_currentFolderPath))
            {
                TryRemoveFolderTreeNodeByPath(work);
                await RefreshBrowserPaneAfterWizardImageDeletesAsync(
                        Array.Empty<WizardPredeletedFileStat>(),
                        new BrowserTreeRefocusAfterWizardContext(preferredNext))
                    .ConfigureAwait(true);
            }

            return true;
        }
        catch (Exception ex)
        {
            if (_deferredWizardBatchRefocusContext != null)
            {
                IReadOnlyList<WizardPredeletedFileStat> stats = _deferredWizardBatchSucceededStats != null
                    ? _deferredWizardBatchSucceededStats
                    : Array.Empty<WizardPredeletedFileStat>();
                await RefreshBrowserPaneAfterWizardImageDeletesAsync(stats, _deferredWizardBatchRefocusContext)
                    .ConfigureAwait(true);
                ClearDeferredWizardBatchBrowserRefreshCapture();
            }

            var detail = "Move failed: " + ex.Message;
            SetTransientStatus(detail);
            ActiveDeleteArchiveWizardPanel?.ShowWizardOperationInfo("Move to archive", detail, InfoBarSeverity.Error);
            return false;
        }
        finally
        {
            LeaveBrowserPaneMutation();
        }
    }

    internal async Task<bool> TryConfirmSendFolderPathToRecycleBinDialogAsync(string work)
    {
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

        int? imageCountInTree = null;
        try
        {
            var imgs = await FolderImagePathCollection.CollectAsync(
                    AppServices.FileSystem,
                    work,
                    includeSubfolders: true)
                .ConfigureAwait(true);
            imageCountInTree = imgs.Count;
        }
        catch
        {
            // count omitted; red line still warns
        }

        var deleteFolderRed = new SolidColorBrush(Microsoft.UI.Colors.Red);
        var deleteFolderPanel = new StackPanel();
        deleteFolderPanel.Children.Add(
            new TextBlock
            {
                Text = string.Join("\n\n", deleteFolderParts),
                TextWrapping = TextWrapping.WrapWholeWords,
            });
        deleteFolderPanel.Children.Add(
            new TextBlock
            {
                Text = imageCountInTree is int c and > 0
                    ? $"{c} image file(s) will be deleted in this folder."
                    : "All contents of this folder will be sent to the Recycle Bin.",
                Foreground = deleteFolderRed,
                TextWrapping = TextWrapping.WrapWholeWords,
                Margin = new Thickness(0, 12, 0, 0),
            });

        var dlg = new ContentDialog
        {
            Title = "Delete folder?",
            Content = deleteFolderPanel,
            PrimaryButtonText = "Delete folder",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot,
        };
        return await ShowWizardContentDialogAsync(dlg).ConfigureAwait(true) == ContentDialogResult.Primary;
    }

    internal async Task<bool> ExecuteSendFolderToRecycleBinAfterConfirmAsync(string work)
    {
        var preferredNext = await TryComputePreferredNextSiblingFolderAsync(work).ConfigureAwait(true);

        try
        {
            EnterBrowserPaneMutation();
            ShellRecycle.SendDirectoryToRecycleBin(work);
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

            SetTransientStatus("Folder sent to the Recycle Bin.");
            _deleteArchiveWizardCapturedWorkingFolder = null;
            var parent = Directory.GetParent(work)?.FullName;
            if (string.Equals(_currentFolderPath, work, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                {
                    await ReconcileBrowserPaneAfterWizardNavigateToParentAsync(
                            parent,
                            new BrowserTreeRefocusAfterWizardContext(preferredNext))
                        .ConfigureAwait(true);
                }
                else
                {
                    FolderTree.RootNodes.Clear();
                    _browseNavAnchorPath = null;
                    _currentFolderPath = null;
                    UpdateBrowserToolbar();
                    _session.LastBrowseFolder = null;
                    _session.LastSelectedImage = null;
                    _session.BrowserTree = null;
                    ClearImageSelectionAndPreviewCore();
                    PersistLayout();
                }
            }
            else if (!string.IsNullOrEmpty(_currentFolderPath))
            {
                TryRemoveFolderTreeNodeByPath(work);
                await RefreshBrowserPaneAfterWizardImageDeletesAsync(
                        Array.Empty<WizardPredeletedFileStat>(),
                        new BrowserTreeRefocusAfterWizardContext(preferredNext))
                    .ConfigureAwait(true);
            }

            return true;
        }
        catch (Exception ex)
        {
            SetTransientStatus("Delete folder failed: " + ex.Message);
            return false;
        }
        finally
        {
            LeaveBrowserPaneMutation();
        }
    }

    internal async Task<bool> WizardExecuteDeleteWorkingFolderToRecycleAsync()
    {
        var work = TryGetDeleteArchiveWizardWorkingFolder();
        if (string.IsNullOrEmpty(work))
        {
            const string msg =
                "Could not resolve the working folder. Select an image in the folder, or close and reopen the wizard.";
            SetTransientStatus(msg);
            ActiveDeleteArchiveWizardPanel?.ShowWizardOperationInfo("Working folder", msg, InfoBarSeverity.Warning);
            return false;
        }

        if (!await TryConfirmSendFolderPathToRecycleBinDialogAsync(work).ConfigureAwait(true))
            return false;

        return await ExecuteSendFolderToRecycleBinAfterConfirmAsync(work).ConfigureAwait(true);
    }

    /// <returns><see langword="false"/> if the user cancelled the permanent-delete preflight; otherwise <see langword="true"/>.</returns>
    private async Task<bool> WizardExecuteImageRecycleOrPermanentBatchAsync(
        IReadOnlyList<string> toDelete,
        bool recordUndoForRecycledPaths,
        string operationNameForLog,
        string? workingFolderOverride = null,
        bool deferBrowserPaneRefresh = false,
        bool assumePermanentFallbackForRecycleFailures = false)
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

        var work = workingFolderOverride ?? TryGetDeleteArchiveWizardWorkingFolder() ?? string.Empty;
        var preNeedsPermanentDialog = WizardImageDeletionPreflight.SuggestsPermanentMayBeNeeded(work);

        var userApprovedPermanentForFailures = false;
        if (preNeedsPermanentDialog)
        {
            if (assumePermanentFallbackForRecycleFailures)
                userApprovedPermanentForFailures = true;
            else
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
                if (await ShowWizardContentDialogAsync(preDlg) != ContentDialogResult.Primary)
                    return false;
                userApprovedPermanentForFailures = true;
            }
        }

        var userApprovedMidBatchPermanent = false;
        var entries = new List<OperationLogEntry>();
        var toRecycleBin = 0;
        var permanentDeleted = 0;
        var skippedDeclined = 0;
        var failed = 0;

        var preByPath = new Dictionary<string, WizardPredeletedFileStat>(StringComparer.OrdinalIgnoreCase);
        var existedAtStart = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var scanLock = new object();
        Parallel.ForEach(
            toDelete,
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            p =>
            {
                if (string.IsNullOrWhiteSpace(p))
                    return;
                try
                {
                    var fi = new FileInfo(p);
                    if (!fi.Exists)
                    {
                        lock (scanLock)
                            existedAtStart[p] = false;
                        return;
                    }

                    var st = new WizardPredeletedFileStat(p, fi.Length, ImageExtensions.IsImageFile(p));
                    lock (scanLock)
                    {
                        existedAtStart[p] = true;
                        preByPath[p] = st;
                    }
                }
                catch
                {
                    lock (scanLock)
                        existedAtStart[p] = false;
                }
            });

        var succeededStats = new List<WizardPredeletedFileStat>();

        EnterBrowserPaneMutation();
        try
        {
            const int permanentDeleteParallelism = 6;

            var onDisk = toDelete.Where(p => existedAtStart.TryGetValue(p, out var ex) && ex).ToList();

            var recycledOk = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (onDisk.Count > 0)
            {
                if (ShellBulkRecycle.TryPerformBatchRecycleToBin(onDisk))
                {
                    foreach (var p in onDisk)
                        recycledOk[p] = !File.Exists(p);
                }
                else
                {
                    foreach (var p in onDisk)
                        recycledOk[p] = ShellRecycle.TrySendFileToRecycleBin(p);
                }
            }

            foreach (var p in toDelete)
            {
                if (!existedAtStart.TryGetValue(p, out var existed) || !existed)
                    continue;

                if (recycledOk.TryGetValue(p, out var rec) && rec)
                {
                    toRecycleBin++;
                    entries.Add(new OperationLogEntry { Path = p, Result = "Ok" });
                    if (preByPath.TryGetValue(p, out var st))
                        succeededStats.Add(st);
                    if (recordUndoForRecycledPaths &&
                        !_wizardSessionUndoRecycledPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
                        _wizardSessionUndoRecycledPaths.Add(p);
                }
            }

            var stuck = toDelete
                .Where(p => existedAtStart.TryGetValue(p, out var ex) && ex && !(recycledOk.TryGetValue(p, out var r) && r))
                .ToList();

            if (stuck.Count > 0)
            {
                if (assumePermanentFallbackForRecycleFailures
                    && !userApprovedPermanentForFailures
                    && !userApprovedMidBatchPermanent)
                    userApprovedMidBatchPermanent = true;

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
                    if (await ShowWizardContentDialogAsync(midDlg) != ContentDialogResult.Primary)
                    {
                        foreach (var p in stuck)
                        {
                            skippedDeclined++;
                            entries.Add(new OperationLogEntry
                            {
                                Path = p,
                                Result = "Skipped",
                                Detail = "Permanent delete declined",
                            });
                        }
                    }
                    else
                    {
                        userApprovedMidBatchPermanent = true;
                    }
                }

                if (userApprovedPermanentForFailures || userApprovedMidBatchPermanent)
                {
                    var stuckSet = stuck.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var permDetail = new ConcurrentDictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    await Parallel.ForEachAsync(
                        stuck,
                        new ParallelOptions { MaxDegreeOfParallelism = permanentDeleteParallelism },
                        async (p, ct) =>
                        {
                            try
                            {
                                await Task.Run(
                                        () =>
                                        {
                                            if (File.Exists(p))
                                                File.Delete(p);
                                        },
                                        ct)
                                    .ConfigureAwait(false);
                                permDetail[p] = null;
                            }
                            catch (Exception ex)
                            {
                                permDetail[p] = ex.Message;
                            }
                        }).ConfigureAwait(true);

                    foreach (var p in toDelete)
                    {
                        if (!stuckSet.Contains(p))
                            continue;
                        if (!permDetail.TryGetValue(p, out var detail) || detail != null)
                        {
                            failed++;
                            entries.Add(new OperationLogEntry
                            {
                                Path = p,
                                Result = "Failed",
                                Detail = detail ?? "Unknown error",
                            });
                            continue;
                        }

                        permanentDeleted++;
                        entries.Add(new OperationLogEntry { Path = p, Result = "Ok", Detail = "Permanent delete" });
                        if (preByPath.TryGetValue(p, out var st))
                            succeededStats.Add(st);
                    }
                }
            }

        if (recordUndoForRecycledPaths && permanentDeleted > 0)
            _wizardSessionHadPermanentImageDeletes = true;

        if (entries.Count > 0)
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
        {
            string? imageDeletionWorkingFolder = null;
            if (!string.IsNullOrEmpty(work)
                && Directory.Exists(work)
                && IsSameOrDescendantDirectory(_currentFolderPath, work))
                imageDeletionWorkingFolder = work;

            if (deferBrowserPaneRefresh)
            {
                _deferredWizardBatchSucceededStats = new List<WizardPredeletedFileStat>(succeededStats);
                _deferredWizardBatchRefocusContext =
                    new BrowserTreeRefocusAfterWizardContext(null, imageDeletionWorkingFolder);
            }
            else
            {
                await RefreshBrowserPaneAfterWizardImageDeletesAsync(
                        succeededStats,
                        new BrowserTreeRefocusAfterWizardContext(null, imageDeletionWorkingFolder))
                    .ConfigureAwait(true);
            }
        }

        ActiveDeleteArchiveWizardPanel?.RefreshUndoAndNoticeUi();
        return true;
        }
        finally
        {
            LeaveBrowserPaneMutation();
        }
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
            var restoredPaths = new List<string>();
            foreach (var p in list)
            {
                if (RecycleBinRestore.TryRestoreOriginalPath(p))
                {
                    restored++;
                    restoredPaths.Add(p);
                }
            }

            _wizardSessionUndoRecycledPaths.Clear();
            SetTransientStatus($"Restored {restored} of {list.Count} file(s) from the Recycle Bin.");
            if (!string.IsNullOrEmpty(_currentFolderPath))
                await RefreshBrowserPaneAfterWizardUndoAsync(restoredPaths).ConfigureAwait(true);
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

        Activate();
    }

    private async Task<ContentDialogResult> ShowWizardContentDialogAsync(ContentDialog dialog)
    {
        Interlocked.Increment(ref _contentDialogModalDepth);
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            Interlocked.Decrement(ref _contentDialogModalDepth);
        }
    }
}