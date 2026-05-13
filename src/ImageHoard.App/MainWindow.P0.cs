using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ImageHoard.Core.Browse;
using ImageHoard.Core.Input;
using ImageHoard.Core.Logging;
using ImageHoard.Core.Metrics;
using ImageHoard.Core.Services;
using ImageHoard.Core.Slideshow;
using ImageHoard.Core.Sort;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;

namespace ImageHoard.App;

public sealed partial class MainWindow
{
    private readonly Stack<(string Path, SortFlagState Previous)> _sortUndoStack = new();
    private SlideshowCoordinator? _slideshow;
    private bool _slideshowUiActive;
    private ImageFitMode _fitMode = ImageFitMode.Fit;
    private readonly SortSession _sortSession = new();

    private enum ImageFitMode
    {
        Fit,
        Fill,
        OneToOne,
    }

    internal bool TryHandleSlideshowKeys(KeyRoutedEventArgs e)
    {
        if (!_slideshowUiActive || _slideshow == null)
            return false;

        if (e.Key == VirtualKey.Left)
        {
            if (_slideshow.TryMovePrevious(out var p) && p != null)
                EnqueuePreviewNavigation(p, true);
            e.Handled = true;
            return true;
        }

        if (e.Key == VirtualKey.Right)
        {
            if (_slideshow.TryMoveNext(out var p) && p != null)
                EnqueuePreviewNavigation(p, true);
            e.Handled = true;
            return true;
        }

        if (e.Key == VirtualKey.T)
        {
            _ = SlideshowToggleScopeFromKeysAsync();
            e.Handled = true;
            return true;
        }

        if (e.Key == VirtualKey.R)
        {
            _slideshow.Tree.Reshuffle();
            e.Handled = true;
            return true;
        }

        return false;
    }

    private async Task SlideshowToggleScopeFromKeysAsync()
    {
        if (_slideshow == null)
            return;
        await _slideshow.ToggleScopeAsync(AppServices.FileSystem, _currentImageFullPath).ConfigureAwait(true);
        if (_slideshow.Scope == SlideshowScopeKind.Folder && _slideshow.GetCurrentPath() is { } p)
            await CommitPreviewImmediatelyAsync(p).ConfigureAwait(true);
        UpdateSlideshowScopeBadge();
    }

    private void InitializeFeatures()
    {
        IncludeSubfoldersToggle.IsChecked = _layoutState.IncludeSubfoldersInList;
        UpdateSortMenuChecks();
        UpdateFileDetailsMenuChecks();
        UpdatePreviewStretch();
        UpdateFitModeMenuChecks();
        ApplyBrowserFileDetailsChrome();
        TryLoadInputProfile();
    }

    private string SortFlagLabel(string path)
    {
        return _sortSession.GetState(path) switch
        {
            SortFlagState.Keep => "K",
            SortFlagState.Delete => "D",
            _ => "·",
        };
    }

    private static readonly SolidColorBrush SortFlagKeepListBrush = new(Color.FromArgb(255, 76, 175, 80));
    private static readonly SolidColorBrush SortFlagDeleteListBrush = new(Color.FromArgb(255, 232, 17, 35));

    private void ApplySortFlagPresentationToRow(ImageRow row, string fullPath)
    {
        row.SortFlagDisplay = SortFlagLabel(fullPath);
        switch (_sortSession.GetState(fullPath))
        {
            case SortFlagState.Keep:
                row.SortFlagGlyphVisibility = Visibility.Visible;
                row.SortFlagGlyphSymbol = Symbol.Accept;
                row.SortFlagGlyphForeground = SortFlagKeepListBrush;
                break;
            case SortFlagState.Delete:
                row.SortFlagGlyphVisibility = Visibility.Visible;
                row.SortFlagGlyphSymbol = Symbol.Cancel;
                row.SortFlagGlyphForeground = SortFlagDeleteListBrush;
                break;
            default:
                row.SortFlagGlyphVisibility = Visibility.Collapsed;
                break;
        }
    }

    private void TryLoadInputProfile()
    {
        try
        {
            var builtin = InputProfileBootstrap.TryLoadCombinedShippedBuiltin();
            if (builtin == null)
                return;

            var userJson = File.Exists(AppDataPaths.UserInputOverridesPath)
                ? File.ReadAllText(AppDataPaths.UserInputOverridesPath)
                : null;
            MergedInputProfile = InputProfileMerger.MergeWithUserOverrides(builtin, userJson);
            KeyboardDispatchTable = InputKeyboardDispatchTable.FromProfile(MergedInputProfile);
            var conflicts = InputBindingConflictChecker.FindChordKeyConflicts(MergedInputProfile);
            if (conflicts.Count > 0)
                SetTransientStatus("Input profile warnings: " + conflicts[0]);
        }
        catch
        {
            // ignore
        }
    }

    internal void UpdateSortMenuChecks()
    {
        SortNameNaturalItem.IsChecked = _layoutState.ListSort == ListSortKind.NameNatural;
        SortNameItem.IsChecked = _layoutState.ListSort == ListSortKind.Name;
        SortDateItem.IsChecked = _layoutState.ListSort == ListSortKind.DateModified;
        SortSizeItem.IsChecked = _layoutState.ListSort == ListSortKind.Size;
    }

    private void UpdateFileDetailsMenuChecks()
    {
        ShowBrowserFileColumnHeadingsToggle.IsChecked = _layoutState.ShowBrowserFileColumnHeadings;
        ShowBrowserFileSizeToggle.IsChecked = _layoutState.ShowBrowserFileSize;
        ShowBrowserFileDateToggle.IsChecked = _layoutState.ShowBrowserFileDate;
    }

    private void ShowBrowserFileColumnHeadingsToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem t)
        {
            _layoutState.ShowBrowserFileColumnHeadings = t.IsChecked == true;
            PersistLayout();
            SyncBrowserFileListHeaderNodes();
            ApplyBrowserFileDetailsChrome();
        }
    }

    private void ShowBrowserFileSizeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem t)
        {
            _layoutState.ShowBrowserFileSize = t.IsChecked == true;
            PersistLayout();
            ApplyBrowserFileDetailsChrome();
        }
    }

    private void ShowBrowserFileDateToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem t)
        {
            _layoutState.ShowBrowserFileDate = t.IsChecked == true;
            PersistLayout();
            ApplyBrowserFileDetailsChrome();
        }
    }

    private void UpdateFitModeMenuChecks()
    {
        FitModeFitItem.IsChecked = _fitMode == ImageFitMode.Fit;
        FitModeFillItem.IsChecked = _fitMode == ImageFitMode.Fill;
        FitModeOneToOneItem.IsChecked = _fitMode == ImageFitMode.OneToOne;
    }

    private void ApplyFitModeUi()
    {
        UpdatePreviewStretch();
        UpdateFitModeMenuChecks();
        if (PreviewImage.Source != null)
            _ = ReloadCurrentPreviewAsync();
    }

    private void UpdatePreviewStretch()
    {
        var stretch = _fitMode switch
        {
            ImageFitMode.Fill => Stretch.UniformToFill,
            ImageFitMode.OneToOne => Stretch.None,
            _ => Stretch.Uniform,
        };
        FullscreenImage.Stretch = stretch;
        UpdatePreviewScrollMetrics();
    }

    private void IncludeSubfoldersToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem t)
        {
            _layoutState.IncludeSubfoldersInList = t.IsChecked == true;
            PersistLayout();
        }
    }

    private void SortList_NameNatural_Click(object sender, RoutedEventArgs e)
    {
        _layoutState.ListSort = ListSortKind.NameNatural;
        PersistLayout();
        UpdateSortMenuChecks();
        if (!string.IsNullOrEmpty(_currentFolderPath))
            _ = RefreshBrowserTreeFromSettingsAsync();
    }

    private void SortList_Name_Click(object sender, RoutedEventArgs e)
    {
        _layoutState.ListSort = ListSortKind.Name;
        PersistLayout();
        UpdateSortMenuChecks();
        if (!string.IsNullOrEmpty(_currentFolderPath))
            _ = RefreshBrowserTreeFromSettingsAsync();
    }

    private void SortList_Date_Click(object sender, RoutedEventArgs e)
    {
        _layoutState.ListSort = ListSortKind.DateModified;
        PersistLayout();
        UpdateSortMenuChecks();
        if (!string.IsNullOrEmpty(_currentFolderPath))
            _ = RefreshBrowserTreeFromSettingsAsync();
    }

    private void SortList_Size_Click(object sender, RoutedEventArgs e)
    {
        _layoutState.ListSort = ListSortKind.Size;
        PersistLayout();
        UpdateSortMenuChecks();
        if (!string.IsNullOrEmpty(_currentFolderPath))
            _ = RefreshBrowserTreeFromSettingsAsync();
    }

    private void ViewFit_Fit_Click(object sender, RoutedEventArgs e)
    {
        _fitMode = ImageFitMode.Fit;
        ApplyFitModeUi();
    }

    private void ViewFit_Fill_Click(object sender, RoutedEventArgs e)
    {
        _fitMode = ImageFitMode.Fill;
        ApplyFitModeUi();
    }

    private void ViewFit_OneToOne_Click(object sender, RoutedEventArgs e)
    {
        _fitMode = ImageFitMode.OneToOne;
        ApplyFitModeUi();
    }

    private Task ReloadCurrentPreviewAsync()
    {
        if (GetSelectedImageRow() is not { } row)
            return Task.CompletedTask;
        EnqueuePreviewNavigation(row.FullPath, false);
        return Task.CompletedTask;
    }

    private async void BrowseGoToPath_Click(object sender, RoutedEventArgs e)
    {
        var box = new TextBox { PlaceholderText = @"C:\path or \\server\share\path", Width = 400 };
        var dlg = new ContentDialog
        {
            Title = "Go to path",
            Content = box,
            PrimaryButtonText = "Open",
            CloseButtonText = "Cancel",
            XamlRoot = RootGrid.XamlRoot,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary)
            return;

        var path = box.Text?.Trim();
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            SetTransientStatus("Path not found.");
            return;
        }

        await NavigateToFolderAsync(path).ConfigureAwait(true);
    }

    private void BrowseAddFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentFolderPath))
            return;
        AddFolderToFavorites(_currentFolderPath);
    }

    private void AddFolderToFavorites(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            SetTransientStatus("Folder not found.");
            return;
        }

        if (_session.Favorites.Contains(folderPath, StringComparer.OrdinalIgnoreCase))
        {
            SetTransientStatus("Already in favorites.");
            return;
        }

        _session.Favorites.Add(folderPath);
        PersistLayout();
        RebuildBrowseFavoritesMenu();
        SetTransientStatus("Added to favorites.");
    }

    private void RemoveFolderFromFavorites(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath))
            return;

        var match = _session.Favorites.FirstOrDefault(p =>
            string.Equals(p, folderPath, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            SetTransientStatus("Favorite not found.");
            return;
        }

        _session.Favorites.Remove(match);
        PersistLayout();
        RebuildBrowseFavoritesMenu();
        SetTransientStatus("Removed from favorites.");
    }

    private void RebuildBrowseFavoritesMenu()
    {
        FileFavoritesSubMenu.Items.Clear();
        if (_session.Favorites.Count == 0)
        {
            FileFavoritesSubMenu.Items.Add(new MenuFlyoutItem { Text = "(none)", IsEnabled = false });
            return;
        }

        foreach (var path in _session.Favorites.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var pathCopy = path;
            var label = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(label))
                label = pathCopy;
            var item = new MenuFlyoutItem { Text = label };
            ToolTipService.SetToolTip(item, pathCopy);
            item.Click += async (_, _) => await NavigateToFolderAsync(pathCopy).ConfigureAwait(true);

            // Use ContextRequested + manual ShowAt instead of ContextFlyout so File > Favorites
            // stays open until this flyout is dismissed or Remove is chosen (default ContextFlyout
            // dismisses the parent menu when the secondary flyout opens).
            item.ContextRequested += (favoriteSender, args) =>
            {
                args.Handled = true;
                var removeFlyout = new MenuFlyout();
                var removeFromFavorites = new MenuFlyoutItem { Text = "Remove from favorites" };
                removeFromFavorites.Click += (_, _) =>
                {
                    var p = pathCopy;
                    _ = this.DispatcherQueue.TryEnqueue(
                        Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal,
                        () => RemoveFolderFromFavorites(p));
                };
                removeFlyout.Items.Add(removeFromFavorites);
                var showOptions = new FlyoutShowOptions { Placement = FlyoutPlacementMode.Auto };
                if (args.TryGetPosition(item, out var pt))
                    showOptions.Position = pt;
                removeFlyout.ShowAt(item, showOptions);
            };

            FileFavoritesSubMenu.Items.Add(item);
        }
    }

    private void TryRevealPathInExplorer(string path, bool isDirectory)
    {
        if (string.IsNullOrEmpty(path))
            return;
        try
        {
            if (isDirectory)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true,
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true,
                });
            }
        }
        catch (Exception ex)
        {
            SetTransientStatus("Reveal failed: " + ex.Message);
        }
    }

    private void BrowseRevealInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedImageRow() is not { } row)
        {
            SetTransientStatus("Select a file first.");
            return;
        }

        TryRevealPathInExplorer(row.FullPath, isDirectory: false);
    }

    private async void BrowseComputeMetrics_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentFolderPath))
            return;
        SetTransientStatus("Computing folder metrics…");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var snap = await FolderMetricsScanner.ScanSubtreeAsync(AppServices.FileSystem, _currentFolderPath, cts.Token);
            await FolderMetricsCacheStore.AppendSnapshotAsync(AppDataPaths.FolderMetricsCachePath, snap, cts.Token);
            SetTransientStatus(
                $"Metrics: {snap.ImageFileCount} images, {snap.TotalFileCount} files, {snap.AggregateSizeBytes / (1024 * 1024)} MiB (cached)");
        }
        catch (Exception ex)
        {
            SetTransientStatus("Metrics failed: " + ex.Message);
        }
    }

    private async void SlideshowStart_Click(object sender, RoutedEventArgs e) =>
        await StartSlideshowFromTreeRootAsync(_currentFolderPath).ConfigureAwait(true);

    /// <summary>Tree-scope slideshow from <paramref name="rootDirectory"/> including subfolders (Algorithm A).</summary>
    private async Task StartSlideshowFromTreeRootAsync(string? rootDirectory, string? missingFolderMessage = null)
    {
        if (string.IsNullOrEmpty(rootDirectory))
        {
            SetTransientStatus(missingFolderMessage ?? "Select a folder first.");
            return;
        }

        if (!Directory.Exists(rootDirectory))
        {
            SetTransientStatus("Folder not found.");
            return;
        }

        var tree = new TreeSlideshowSession(AppServices.FileSystem);
        _slideshow = new SlideshowCoordinator(tree);
        _slideshow.Tree.Start(rootDirectory);
        _slideshowUiActive = true;
        SetTransientStatus("Starting slideshow…");
        try
        {
            await _slideshow.Tree.WaitForInitialPoolAsync();
        }
        catch
        {
            // ignored
        }

        if (!_slideshow.Tree.TryMoveNext(out var first) || first == null)
        {
            SetTransientStatus("No images under that folder.");
            _slideshowUiActive = false;
            _slideshow = null;
            return;
        }

        await CommitPreviewImmediatelyAsync(first);
        if (!_isFullscreen)
            ToggleFullscreen();
        UpdateSlideshowScopeBadge();
    }

    private void UpdateSlideshowScopeBadge()
    {
        if (FullscreenScopeBadgeHost == null)
            return;
        if (!_slideshowUiActive || _slideshow == null)
        {
            FullscreenScopeBadgeHost.Visibility = Visibility.Collapsed;
            return;
        }

        FullscreenScopeBadgeHost.Visibility = Visibility.Visible;
        FullscreenScopeBadge.Text = _slideshow.Scope == SlideshowScopeKind.Tree ? "TREE" : "FOLDER";
    }

    private async void SlideshowToggleScope_Click(object sender, RoutedEventArgs e)
    {
        if (_slideshow == null || !_slideshowUiActive)
            return;
        await _slideshow.ToggleScopeAsync(AppServices.FileSystem, _currentImageFullPath).ConfigureAwait(true);
        if (_slideshow.Scope == SlideshowScopeKind.Folder && _slideshow.GetCurrentPath() is { } p)
            await CommitPreviewImmediatelyAsync(p).ConfigureAwait(true);
        UpdateSlideshowScopeBadge();
    }

    private void SlideshowReshuffle_Click(object sender, RoutedEventArgs e)
    {
        _slideshow?.Tree.Reshuffle();
        SetTransientStatus("Slideshow reshuffled.");
    }

    private async void SlideshowFairnessHelp_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ContentDialog
        {
            Title = "Random order fairness",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.WrapWholeWords,
                MaxWidth = 420,
                Text =
                    "Random slideshow picks images from a running sample of files discovered so far under your chosen folder. Until enough files have been discovered, order is approximately random; over time it evens out. Reshuffle starts a new random session.",
            },
            CloseButtonText = "OK",
            XamlRoot = RootGrid.XamlRoot,
        };
        await dlg.ShowAsync();
    }

    private void SortFlagKeep_Click(object sender, RoutedEventArgs e) => SetSelectedSortFlag(SortFlagState.Keep);

    private void SortFlagDelete_Click(object sender, RoutedEventArgs e) => SetSelectedSortFlag(SortFlagState.Delete);

    private void SortFlagUnset_Click(object sender, RoutedEventArgs e) => SetSelectedSortFlag(SortFlagState.Unset);

    private void SetSelectedSortFlag(SortFlagState state)
    {
        if (GetSelectedImageRow() is not { } row)
            return;
        var current = _sortSession.GetState(row.FullPath);
        var resolved = SortFlagInput.ResolveToggle(current, state);
        _sortUndoStack.Push((row.FullPath, current));
        _sortSession.SetState(row.FullPath, resolved);
        UpdatePathOverlays();
        RefreshSortFlagDisplayInList(row.FullPath);
    }

    private void SortUndoLastFlagFromInput()
    {
        if (_sortUndoStack.Count == 0)
        {
            SetTransientStatus("Nothing to undo.");
            return;
        }

        var (path, prev) = _sortUndoStack.Pop();
        _sortSession.SetState(path, prev);
        UpdatePathOverlays();
        RefreshSortFlagDisplayInList(path);
        SetTransientStatus("Undid last flag change.");
    }

    private void ViewCycleFitFromInput()
    {
        _fitMode = (ImageFitMode)(((int)_fitMode + 1) % 3);
        ApplyFitModeUi();
    }

    private void ToggleIncludeSubfoldersFromInput()
    {
        _layoutState.IncludeSubfoldersInList = !_layoutState.IncludeSubfoldersInList;
        IncludeSubfoldersToggle.IsChecked = _layoutState.IncludeSubfoldersInList;
        PersistLayout();
    }

    private void OptionsPreferences_Click(object sender, RoutedEventArgs e) =>
        PreferencesWindow.ShowOrActivate(this);

    private async void SortBatchDelete_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentFolderPath))
            return;
        List<string> paths;
        try
        {
            paths = await FolderImagePathCollection.CollectAsync(
                AppServices.FileSystem,
                _currentFolderPath,
                _layoutState.IncludeSubfoldersInList).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetTransientStatus("Could not list images: " + ex.Message);
            return;
        }

        if (paths.Count == 0)
            return;
        var (keep, delete, unset) = _sortSession.CountStates(paths);
        if (unset > 0)
        {
            var block = new ContentDialog
            {
                Title = "Can't delete yet",
                Content = new TextBlock
                {
                    Text =
                        $"You still have {unset} image(s) with no decision. Mark each image Keep or Delete before running batch delete.",
                    TextWrapping = TextWrapping.WrapWholeWords,
                },
                CloseButtonText = "OK",
                XamlRoot = RootGrid.XamlRoot,
            };
            await block.ShowAsync();
            return;
        }

        var notKeep = paths.Count(p => _sortSession.GetState(p) != SortFlagState.Keep);
        var confirm = new ContentDialog
        {
            Title = "Delete non-keepers?",
            Content = new TextBlock
            {
                Text =
                    $"Recycle Bin will receive {notKeep} image(s) (not marked Keep). {keep} image(s) marked Keep are unchanged.",
                TextWrapping = TextWrapping.WrapWholeWords,
            },
            PrimaryButtonText = $"Delete {notKeep} image(s)",
            CloseButtonText = "Cancel",
            XamlRoot = RootGrid.XamlRoot,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            return;

        if (!BatchDeletePlanner.TryGetDeletionSet(paths, _sortSession, out var toDelete, out _))
            return;

        var entries = new List<OperationLogEntry>();
        var ok = 0;
        var failed = 0;
        foreach (var p in toDelete!)
        {
            try
            {
                ShellRecycle.SendFileToRecycleBin(p);
                ok++;
                entries.Add(new OperationLogEntry { Path = p, Result = "Ok" });
            }
            catch (Exception ex)
            {
                failed++;
                entries.Add(new OperationLogEntry { Path = p, Result = "Failed", Detail = ex.Message });
            }
        }

        if (_session.LogDestructiveOperations)
        {
            var rec = new OperationLogBatchRecord
            {
                Operation = "BatchDelete",
                Summary = new OperationLogSummary { Ok = ok, Failed = failed, Skipped = 0 },
                Entries = entries,
            };
            try
            {
                await OperationLogWriter.AppendAsync(AppDataPaths.OperationsLogPath, rec);
            }
            catch
            {
                // ignored
            }
        }

        SetTransientStatus($"Sent {ok} image(s) to the Recycle Bin (not marked Keep).");
        if (!string.IsNullOrEmpty(_currentFolderPath))
            await NavigateToFolderAsync(_currentFolderPath).ConfigureAwait(true);
    }

    private async void SortMoveArchive_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentFolderPath))
            return;
        if (string.IsNullOrEmpty(_session.ArchiveRoot))
        {
            SetTransientStatus("Set archive root in Settings first.");
            return;
        }

        var name = new DirectoryInfo(_currentFolderPath).Name;
        var dest = Path.Combine(_session.ArchiveRoot!, name);
        var dlg = new ContentDialog
        {
            Title = "Move to archive",
            Content = new TextBlock
            {
                Text = $"Move folder:\n{_currentFolderPath}\n→\n{dest}",
                TextWrapping = TextWrapping.WrapWholeWords,
            },
            PrimaryButtonText = "Move",
            CloseButtonText = "Cancel",
            XamlRoot = RootGrid.XamlRoot,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary)
            return;

        try
        {
            await AppServices.FileSystem.MoveDirectoryAsync(_currentFolderPath, dest);
            if (_session.LogDestructiveOperations)
            {
                var rec = new OperationLogBatchRecord
                {
                    Operation = "MoveToArchive",
                    Summary = new OperationLogSummary { Ok = 1, Failed = 0, Skipped = 0 },
                    Entries =
                    {
                        new OperationLogEntry { Path = _currentFolderPath, Result = "Ok", Detail = dest },
                    },
                };
                await OperationLogWriter.AppendAsync(AppDataPaths.OperationsLogPath, rec);
            }

            SetTransientStatus("Folder moved to archive.");
            FolderTree.RootNodes.Clear();
            _browseNavAnchorPath = null;
            _currentFolderPath = null;
            UpdateBrowserToolbar();
            _session.LastBrowseFolder = null;
            _session.LastSelectedImage = null;
            PersistLayout();
        }
        catch (Exception ex)
        {
            SetTransientStatus("Move failed: " + ex.Message);
        }
    }

    private async void SettingsArchiveRoot_Click(object sender, RoutedEventArgs e)
    {
        if (RootGrid.XamlRoot != null)
            await ((IPreferencesSession)this).PromptEditArchiveRootAsync(RootGrid.XamlRoot);
    }

    private void SettingsClearCaches_Click(object sender, RoutedEventArgs e)
    {
        AppSettingsStore.ClearCaches(deleteOperationLog: false);
        SetTransientStatus("Caches cleared (folder metrics).");
    }

    private void SettingsClearCachesAndLog_Click(object sender, RoutedEventArgs e)
    {
        AppSettingsStore.ClearCaches(deleteOperationLog: true);
        SetTransientStatus("Caches and operation log cleared.");
    }

    private void HandleSortKeyboardShortcuts(KeyRoutedEventArgs e)
    {
        if (_isFullscreen || GetSelectedImageRow() is null)
            return;

        if (e.Key == VirtualKey.K)
        {
            SetSelectedSortFlag(SortFlagState.Keep);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.D)
        {
            SetSelectedSortFlag(SortFlagState.Delete);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.U)
        {
            SetSelectedSortFlag(SortFlagState.Unset);
            e.Handled = true;
        }
    }

}
