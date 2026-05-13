using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ImageHoard.Core.Browse;
using ImageHoard.Core.Input;
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

    private void StopSlideshowSession()
    {
        if (!_slideshowUiActive && _slideshow == null)
            return;
        _slideshowUiActive = false;
        _slideshow?.Tree.StopEnumeration();
        _slideshow = null;
        UpdateSlideshowScopeBadge();
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
        UpdateFolderDetailsMenuChecks();
        UpdatePreviewStretch();
        UpdateFitModeMenuChecks();
        ApplyBrowserFileDetailsChrome();
        ApplyBrowserFolderDetailsChrome();
        SyncBrowserFolderListHeaderNodes();
        SyncBrowserFileListHeaderNodes();
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

    private void UpdateFolderDetailsMenuChecks()
    {
        ShowBrowserFolderColumnHeadingsToggle.IsChecked = _layoutState.ShowBrowserFolderColumnHeadings;
        ShowBrowserFolderDateToggle.IsChecked = _layoutState.ShowBrowserFolderDate;
        ShowBrowserFolderSizeToggle.IsChecked = _layoutState.ShowBrowserFolderSize;
        ShowBrowserFolderImageCountToggle.IsChecked = _layoutState.ShowBrowserFolderImageCount;
    }

    private void ShowBrowserFileColumnHeadingsToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem t)
        {
            _layoutState.ShowBrowserFileColumnHeadings = t.IsChecked == true;
            PersistLayout();
            SyncBrowserFolderListHeaderNodes();
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

    private void ShowBrowserFolderColumnHeadingsToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem t)
        {
            _layoutState.ShowBrowserFolderColumnHeadings = t.IsChecked == true;
            PersistLayout();
            SyncBrowserFolderListHeaderNodes();
            ApplyBrowserFolderDetailsChrome();
        }
    }

    private void ShowBrowserFolderDateToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem t)
        {
            _layoutState.ShowBrowserFolderDate = t.IsChecked == true;
            PersistLayout();
            ApplyBrowserFolderDetailsChrome();
        }
    }

    private void ShowBrowserFolderSizeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem t)
        {
            _layoutState.ShowBrowserFolderSize = t.IsChecked == true;
            PersistLayout();
            ApplyBrowserFolderDetailsChrome();
            RefreshAllFolderEntrySizingDisplays();
            if (_layoutState.CalculateFolderSizesInBackground
                && (_layoutState.ShowBrowserFolderSize || _layoutState.ShowBrowserFolderImageCount))
                EnqueueFolderMetricsForAllVisibleFolderPaths();
        }
    }

    private void ShowBrowserFolderImageCountToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem t)
        {
            _layoutState.ShowBrowserFolderImageCount = t.IsChecked == true;
            PersistLayout();
            ApplyBrowserFolderDetailsChrome();
            RefreshAllFolderEntrySizingDisplays();
            if (_layoutState.CalculateFolderSizesInBackground
                && (_layoutState.ShowBrowserFolderSize || _layoutState.ShowBrowserFolderImageCount))
                EnqueueFolderMetricsForAllVisibleFolderPaths();
        }
    }

    private void FolderBrowserHeaderSort_Name_Click(object sender, RoutedEventArgs e)
    {
        _layoutState.FolderListSort = FolderListSortKind.NameNatural;
        PersistLayout();
        ResortAllFolderGroups();
        SyncBrowserFolderListHeaderNodes();
        ApplyBrowserFolderDetailsChrome();
    }

    private void FolderBrowserHeaderSort_Date_Click(object sender, RoutedEventArgs e)
    {
        _layoutState.FolderListSort = FolderListSortKind.DateModified;
        PersistLayout();
        ResortAllFolderGroups();
        SyncBrowserFolderListHeaderNodes();
        ApplyBrowserFolderDetailsChrome();
    }

    private void FolderBrowserHeaderSort_Size_Click(object sender, RoutedEventArgs e)
    {
        _layoutState.FolderListSort = FolderListSortKind.AggregateSize;
        PersistLayout();
        ResortAllFolderGroups();
        SyncBrowserFolderListHeaderNodes();
        ApplyBrowserFolderDetailsChrome();
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
            StopSlideshowSession();
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
        _sortSession.SetState(row.FullPath, resolved);
        UpdatePathOverlays();
        RefreshSortFlagDisplayInList(row.FullPath);
        EnsurePreviewMatchesBrowseNavigationMode();
    }

    private void SortClearAllFlagsFromInput()
    {
        if (_sortSession.States.Count == 0)
        {
            SetTransientStatus("No flags to clear.");
            return;
        }

        _sortSession.Clear();
        UpdatePathOverlays();
        RefreshAllSortFlagDisplaysInList();
        EnsurePreviewMatchesBrowseNavigationMode();
        SetTransientStatus("Cleared all sort flags.");
    }

    private void SortClearAllFlags_Click(object sender, RoutedEventArgs e) => SortClearAllFlagsFromInput();

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
        ShowOrActivatePreferences();

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
