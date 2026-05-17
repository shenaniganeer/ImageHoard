using System.IO;
using System.Linq;
using ImageHoard.App.BrowserV2;
using ImageHoard.Core.Browse;
using ImageHoard.Core.Logging;

namespace ImageHoard.App;

/// <summary>Browse2 internal drag-move (multi-select + cross-pane drop).</summary>
public sealed partial class MainWindow
{
    private void Browse2AttachMoveDropHandlers()
    {
        BrowserV2Host.FolderTree.MoveDropRequested += Browse2OnFolderTreeMoveDropRequested;
        BrowserV2Host.ImagePane.MoveDropRequested += Browse2OnImagePaneMoveDropRequested;
    }

    private void Browse2DetachMoveDropHandlers()
    {
        BrowserV2Host.FolderTree.MoveDropRequested -= Browse2OnFolderTreeMoveDropRequested;
        BrowserV2Host.ImagePane.MoveDropRequested -= Browse2OnImagePaneMoveDropRequested;
    }

    private async void Browse2OnFolderTreeMoveDropRequested(FolderTreeView _, BrowserPaneMoveDropRequestedEventArgs e)
    {
        try
        {
            await Browse2ExecuteMoveDropAsync(e.SourcePaths, e.DestinationDirectory).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetTransientStatus("Move failed: " + ex.Message);
        }
    }

    private async void Browse2OnImagePaneMoveDropRequested(ImagePaneView _, BrowserPaneMoveDropRequestedEventArgs e)
    {
        try
        {
            await Browse2ExecuteMoveDropAsync(e.SourcePaths, e.DestinationDirectory).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetTransientStatus("Move failed: " + ex.Message);
        }
    }

    internal async Task Browse2ExecuteMoveDropAsync(IReadOnlyList<string> sources, string destinationDirectory)
    {
        if (_browse2Coordinator is null)
            return;

        if (IsBrowserPaneMutationInProgress || _browse2Coordinator.Mutations.IsActive)
        {
            SetTransientStatus("Finish the current operation before moving files.");
            return;
        }

        var dest = FavoriteIndexRoots.NormalizeFavoritePath(destinationDirectory);
        if (string.IsNullOrEmpty(dest) || !Directory.Exists(dest))
        {
            SetTransientStatus("Invalid destination folder.");
            return;
        }

        var fs = AppServices.FileSystem;
        var classified = new List<(string Path, bool IsDir)>();
        foreach (var s in sources.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var n = FavoriteIndexRoots.NormalizeFavoritePath(s);
            if (string.IsNullOrEmpty(n))
                continue;
            if (await fs.DirectoryExistsAsync(n).ConfigureAwait(true))
                classified.Add((n, true));
            else if (await fs.FileExistsAsync(n).ConfigureAwait(true))
                classified.Add((n, false));
        }

        if (classified.Count == 0)
        {
            SetTransientStatus("Nothing to move (paths not found).");
            return;
        }

        var block = BrowserPaneMovePathValidation.GetBlockingReason(classified, dest);
        if (block is not null)
        {
            SetTransientStatus(block);
            return;
        }

        var dirMoves = classified.Where(x => x.IsDir).OrderByDescending(x => x.Path.Length).Select(x => x.Path).ToList();
        var fileMoves = classified.Where(x => !x.IsDir).Select(x => x.Path).ToList();

        EnterBrowserPaneMutation();
        try
        {
            var entries = new List<OperationLogEntry>();
            var ok = 0;
            var fail = 0;

            foreach (var src in dirMoves)
            {
                try
                {
                    var leaf = Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    var pick = BrowserPaneRenameHelper.PickUniqueDirectoryName(dest, leaf, src);
                    await fs.MoveDirectoryAsync(src, pick).ConfigureAwait(true);
                    _sortSession.RelocatePathsForDirectoryRename(src, pick);
                    await Browse2NotifyWizardDirectoryMovedAsync(src, pick).ConfigureAwait(true);
                    Browse2RelocateUiPathsAfterSubtreeMove(src, pick);
                    ok++;
                    entries.Add(new OperationLogEntry { Path = src, Result = "Ok", Detail = pick });
                }
                catch (Exception ex)
                {
                    fail++;
                    entries.Add(new OperationLogEntry { Path = src, Result = "Failed", Detail = ex.Message });
                }
            }

            foreach (var src in fileMoves)
            {
                try
                {
                    var name = Path.GetFileName(src);
                    var pick = BrowserPaneRenameHelper.PickUniqueFileName(dest, name, src);
                    await fs.MoveFileAsync(src, pick).ConfigureAwait(true);
                    _sortSession.RelocateImagePath(src, pick);
                    Browse2PatchSessionPathsForSinglePathMove(src, pick);
                    ok++;
                    entries.Add(new OperationLogEntry { Path = src, Result = "Ok", Detail = pick });
                }
                catch (Exception ex)
                {
                    fail++;
                    entries.Add(new OperationLogEntry { Path = src, Result = "Failed", Detail = ex.Message });
                }
            }

            var refreshSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { dest };
            foreach (var src in dirMoves.Concat(fileMoves))
            {
                var p = Path.GetDirectoryName(src);
                if (!string.IsNullOrEmpty(p))
                    refreshSet.Add(p);
            }

            foreach (var p in refreshSet)
            {
                try
                {
                    await _browse2Coordinator.RefreshFolderListingAsync(p, CancellationToken.None).ConfigureAwait(true);
                }
                catch
                {
                    // ignored per-folder
                }
            }

            await RefreshBrowserTreeFromSettingsAsync().ConfigureAwait(true);

            if (_browse2Coordinator != null)
                await _browse2Coordinator.Images.WaitForReloadAppliedAsync(CancellationToken.None).ConfigureAwait(true);

            if (_browse2Coordinator?.Images is { } img)
                img.SelectByPath(_session.LastSelectedImage);

            SyncBrowse2SyntheticPrimaryNavNode();
            UpdatePathOverlays();
            PersistLayout();

            BrowserV2Host.FolderTree.SyncFolderMultiSelectFromBrowsedPath();

            try
            {
                var rec = new OperationLogBatchRecord
                {
                    Operation = "RenameMove",
                    Summary = new OperationLogSummary { Ok = ok, Failed = fail, Skipped = 0 },
                    Entries = entries,
                };
                await OperationLogWriter.AppendAsync(AppDataPaths.OperationsLogPath, rec, CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch
            {
                // ignored
            }

            SetTransientStatus(
                fail > 0
                    ? $"Moved {ok} items; {fail} failed."
                    : ok == 1
                        ? "Moved 1 item."
                        : $"Moved {ok} items.");
        }
        finally
        {
            LeaveBrowserPaneMutation();
        }
    }

    private void Browse2PatchSessionPathsForSinglePathMove(string oldPath, string newPath)
    {
        if (string.Equals(_session.LastSelectedImage, oldPath, StringComparison.OrdinalIgnoreCase))
            _session.LastSelectedImage = newPath;
        if (string.Equals(_session.LastActedFsObject, oldPath, StringComparison.OrdinalIgnoreCase))
            _session.LastActedFsObject = newPath;
        if (string.Equals(_currentImageFullPath, oldPath, StringComparison.OrdinalIgnoreCase))
            _currentImageFullPath = newPath;
        if (string.Equals(_browseNavAnchorPath, oldPath, StringComparison.OrdinalIgnoreCase))
            _browseNavAnchorPath = newPath;
    }

    private void Browse2RelocateUiPathsAfterSubtreeMove(string oldRoot, string newRoot)
    {
        var oldN = oldRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var newN = newRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        static void RelocatePath(ref string? path, string oldP, string newP)
        {
            if (string.IsNullOrEmpty(path))
                return;
            if (!string.Equals(path, oldP, StringComparison.OrdinalIgnoreCase)
                && !BrowserTreeDeletePathDedupe.IsStrictDescendantPath(oldP, path))
                return;

            try
            {
                if (string.Equals(path, oldP, StringComparison.OrdinalIgnoreCase))
                {
                    path = newP;
                    return;
                }

                path = Path.GetFullPath(Path.Combine(newP, Path.GetRelativePath(oldP, path)));
            }
            catch
            {
                // ignored
            }
        }

        RelocatePath(ref _currentFolderPath, oldN, newN);

        var lb = _session.LastBrowseFolder;
        RelocatePath(ref lb, oldN, newN);
        _session.LastBrowseFolder = lb;

        var lsi = _session.LastSelectedImage;
        RelocatePath(ref lsi, oldN, newN);
        _session.LastSelectedImage = lsi;

        var lao = _session.LastActedFsObject;
        RelocatePath(ref lao, oldN, newN);
        _session.LastActedFsObject = lao;

        RelocatePath(ref _currentImageFullPath, oldN, newN);
        RelocatePath(ref _browseNavAnchorPath, oldN, newN);
    }
}
