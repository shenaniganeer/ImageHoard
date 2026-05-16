using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageHoard.Core.Browse;
using ImageHoard.Core.Browse2;
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
    private ImageFitMode _fitMode = ImageFitMode.ShrinkOnly;
    private readonly SortSession _sortSession = new();

    private enum ImageFitMode
    {
        ShrinkOnly = 0,
        ShrinkAndStretch = 1,
        OneToOne = 2,
    }

    private void DiscardSlideshowSession()
    {
        if (!_slideshowUiActive && _slideshow == null)
            return;
        _slideshowUiActive = false;
        _slideshow?.Tree.StopEnumeration();
        _slideshow = null;
        UpdateSlideshowScopeBadge();
    }

    private void SuspendSlideshowUi()
    {
        if (!_slideshowUiActive)
            return;
        _slideshowUiActive = false;
        UpdateSlideshowScopeBadge();
    }

    internal bool HasSuspendedSlideshowSession => _slideshow != null && !_slideshowUiActive;

    private void InitializeFeatures()
    {
        IncludeSubfoldersToggle.IsChecked = _layoutState.IncludeSubfoldersInList;
        UpdateSortMenuChecks();
        UpdateFileDetailsMenuChecks();
        UpdateFolderDetailsMenuChecks();
        UpdatePreviewStretch();
        UpdateFitModeMenuChecks();
        ScheduleDeferredBrowserChromeAfterStartup();
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
            KeyboardDispatchTable = InputKeyboardDispatchTable.FromProfileExcludingCommandIds(
                MergedInputProfile,
                BrowserTreeKeyboardCommandIds.AllTreeCommandIdSet);
            BrowserTreeKeyboardDispatchTable = InputKeyboardDispatchTable.FromProfileIncludingCommandsInOrder(
                MergedInputProfile,
                BrowserTreeKeyboardCommandIds.InDispatchOrder);
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

        FolderSortNameNaturalItem.IsChecked = _layoutState.FolderListSort == FolderListSortKind.NameNatural;
        FolderSortDateItem.IsChecked = _layoutState.FolderListSort == FolderListSortKind.DateModified;
        FolderSortTotalSizeItem.IsChecked = _layoutState.FolderListSort == FolderListSortKind.AggregateSize;
        FolderSortImageCountItem.IsChecked = _layoutState.FolderListSort == FolderListSortKind.ImageFileCount;
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
            if (_layoutState.ShowBrowserFolderSize || _layoutState.ShowBrowserFolderImageCount)
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
            if (_layoutState.ShowBrowserFolderSize || _layoutState.ShowBrowserFolderImageCount)
                EnqueueFolderMetricsForAllVisibleFolderPaths();
        }
    }

    private void ApplyFolderListSort(FolderListSortKind kind)
    {
        CancelFolderResortCoalesceState();
        _layoutState.FolderListSort = kind;
        PersistLayout();
        ResortAllFolderGroups();
        ScheduleViewport(BrowserTreeViewportIntentResolver.ForRootPopulate(BuildBrowserPaneState()));
        ApplyBrowserFolderDetailsChrome();
        UpdateSortMenuChecks();
        if (_browse2Coordinator is not null)
        {
            _ = _browse2Coordinator.Tree.SetFolderSortKind(kind);
            if (kind is FolderListSortKind.AggregateSize or FolderListSortKind.ImageFileCount)
            {
                var root = _browse2Coordinator.Workspace.IndexRoot;
                var sel = _browse2Coordinator.Tree.Model.Selection.SelectedFolderPath;
                var parent = string.IsNullOrEmpty(sel)
                    ? root
                    : FsMapPathHelpers.ParentPathOrEmpty(sel, root);
                if (string.IsNullOrEmpty(parent))
                    parent = root;
                _ = _browse2Coordinator.EnsureAggregatesForVisibleChildrenAsync(parent, CancellationToken.None);
            }
        }
    }

    private void FolderBrowserHeaderSort_Name_Click(object sender, RoutedEventArgs e) =>
        ApplyFolderListSort(FolderListSortKind.NameNatural);

    private void FolderBrowserHeaderSort_Date_Click(object sender, RoutedEventArgs e) =>
        ApplyFolderListSort(FolderListSortKind.DateModified);

    private void FolderBrowserHeaderSort_Size_Click(object sender, RoutedEventArgs e) =>
        ApplyFolderListSort(FolderListSortKind.AggregateSize);

    private void FolderSortMenu_NameNatural_Click(object sender, RoutedEventArgs e) =>
        ApplyFolderListSort(FolderListSortKind.NameNatural);

    private void FolderSortMenu_DateModified_Click(object sender, RoutedEventArgs e) =>
        ApplyFolderListSort(FolderListSortKind.DateModified);

    private void FolderSortMenu_TotalSize_Click(object sender, RoutedEventArgs e) =>
        ApplyFolderListSort(FolderListSortKind.AggregateSize);

    private void FolderSortMenu_ImageCount_Click(object sender, RoutedEventArgs e) =>
        ApplyFolderListSort(FolderListSortKind.ImageFileCount);

    private void FolderBrowserHeaderSort_ImageCount_Click(object sender, RoutedEventArgs e) =>
        ApplyFolderListSort(FolderListSortKind.ImageFileCount);

    private void UpdateFitModeMenuChecks()
    {
        FitModeShrinkOnlyItem.IsChecked = _fitMode == ImageFitMode.ShrinkOnly;
        FitModeShrinkAndStretchItem.IsChecked = _fitMode == ImageFitMode.ShrinkAndStretch;
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
        ApplyFullscreenImageForFitMode();
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
        Browse2ApplyImageListSortFromLayout();
        if (!string.IsNullOrEmpty(_currentFolderPath))
            _ = RefreshBrowserTreeFromSettingsAsync();
    }

    private void SortList_Name_Click(object sender, RoutedEventArgs e)
    {
        _layoutState.ListSort = ListSortKind.Name;
        PersistLayout();
        UpdateSortMenuChecks();
        Browse2ApplyImageListSortFromLayout();
        if (!string.IsNullOrEmpty(_currentFolderPath))
            _ = RefreshBrowserTreeFromSettingsAsync();
    }

    private void SortList_Date_Click(object sender, RoutedEventArgs e)
    {
        _layoutState.ListSort = ListSortKind.DateModified;
        PersistLayout();
        UpdateSortMenuChecks();
        Browse2ApplyImageListSortFromLayout();
        if (!string.IsNullOrEmpty(_currentFolderPath))
            _ = RefreshBrowserTreeFromSettingsAsync();
    }

    private void SortList_Size_Click(object sender, RoutedEventArgs e)
    {
        _layoutState.ListSort = ListSortKind.Size;
        PersistLayout();
        UpdateSortMenuChecks();
        Browse2ApplyImageListSortFromLayout();
        if (!string.IsNullOrEmpty(_currentFolderPath))
            _ = RefreshBrowserTreeFromSettingsAsync();
    }

    private void ViewFit_ShrinkOnly_Click(object sender, RoutedEventArgs e)
    {
        _fitMode = ImageFitMode.ShrinkOnly;
        ApplyFitModeUi();
    }

    private void ViewFit_ShrinkAndStretch_Click(object sender, RoutedEventArgs e)
    {
        _fitMode = ImageFitMode.ShrinkAndStretch;
        ApplyFitModeUi();
    }

    private void ViewFit_OneToOne_Click(object sender, RoutedEventArgs e)
    {
        _fitMode = ImageFitMode.OneToOne;
        ApplyFitModeUi();
    }

    private void ImageZoomIn_Click(object sender, RoutedEventArgs e) => _ = TryExecuteViewZoomIn();

    private void ImageZoomOut_Click(object sender, RoutedEventArgs e) => _ = TryExecuteViewZoomOut();

    private void ImageZoomDefaultFit_Click(object sender, RoutedEventArgs e) => _ = TryExecuteViewZoomResetFit();

    private void ImageZoomOriginalResolution_Click(object sender, RoutedEventArgs e) => RequestViewZoomActualPixelsAsync();

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
        KickFavoriteFilesystemMapBackgroundReconcileForIndexRoots();
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
        KickFavoriteFilesystemMapBackgroundReconcileForIndexRoots();
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
        await PromptResumeOrStartSlideshowAsync().ConfigureAwait(true);

    private async Task PromptResumeOrStartSlideshowAsync()
    {
        if (_slideshowUiActive)
            return;

        if (HasSuspendedSlideshowSession)
        {
            var dlg = new ContentDialog
            {
                Title = "Slideshow",
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.WrapWholeWords,
                    MaxWidth = 420,
                    Text = "Resume the slideshow you left (same random tree session), or start a new tree slideshow from the folder you are browsing now?",
                },
                PrimaryButtonText = "Resume",
                SecondaryButtonText = "New at current folder",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootGrid.XamlRoot,
            };
            var r = await ShowWizardContentDialogAsync(dlg);
            if (r == ContentDialogResult.Primary)
            {
                await ResumeSlideshowUiAsync().ConfigureAwait(true);
                return;
            }

            if (r == ContentDialogResult.Secondary)
            {
                DiscardSlideshowSession();
                await StartSlideshowFromTreeRootAsync(_currentFolderPath).ConfigureAwait(true);
            }

            return;
        }

        await StartSlideshowFromTreeRootAsync(_currentFolderPath).ConfigureAwait(true);
    }

    private async Task ResumeSlideshowUiAsync()
    {
        if (_slideshow == null)
            return;

        _slideshow.ClearSiblingOverlay();
        _slideshowUiActive = true;
        UpdateSlideshowScopeBadge();
        var path = _slideshow.GetCurrentPath();
        if (string.IsNullOrEmpty(path))
            path = _slideshow.Tree.CurrentPath;
        if (!string.IsNullOrEmpty(path))
            await CommitPreviewImmediatelyAsync(path).ConfigureAwait(true);
        if (!_isFullscreen)
            ToggleFullscreen();
    }

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

        DiscardSlideshowSession();
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
            DiscardSlideshowSession();
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
        FullscreenScopeBadge.Text = _slideshow.IsSiblingOverlayActive ? "SIBLINGS" : "TREE";
    }

    internal async Task SwitchToBrowseAtCurrentSlideshowLocationAsync()
    {
        if (!_slideshowUiActive || _slideshow == null || string.IsNullOrEmpty(_currentImageFullPath))
            return;

        var imagePath = _currentImageFullPath;
        var parent = Path.GetDirectoryName(imagePath);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
        {
            SetTransientStatus("Could not open parent folder.");
            return;
        }

        _slideshow.ClearSiblingOverlay();
        SuspendSlideshowUi();
        if (_isFullscreen)
            ExitFullscreenChrome();

        await NavigateToFolderAsync(parent).ConfigureAwait(true);
        await TrySyncBrowseTreeSelectionToImagePathAsync(imagePath).ConfigureAwait(true);
        await CommitPreviewImmediatelyAsync(imagePath).ConfigureAwait(true);
        _session.LastSelectedImage = imagePath;
        SetLastActedFsObject(imagePath);
        UpdateSlideshowScopeBadge();
    }

    private async Task SlideshowSiblingNextFromInputAsync()
    {
        if (!_slideshowUiActive || _slideshow == null)
            return;
        if (string.IsNullOrEmpty(_currentImageFullPath))
            return;
        if (!await _slideshow.TryMoveNextSiblingAsync(AppServices.FileSystem, _currentImageFullPath).ConfigureAwait(true))
            return;
        var p = _slideshow.GetCurrentPath();
        if (!string.IsNullOrEmpty(p))
            EnqueuePreviewNavigation(p, true);
        UpdateSlideshowScopeBadge();
    }

    private async Task SlideshowSiblingPrevFromInputAsync()
    {
        if (!_slideshowUiActive || _slideshow == null)
            return;
        if (string.IsNullOrEmpty(_currentImageFullPath))
            return;
        if (!await _slideshow.TryMovePreviousSiblingAsync(AppServices.FileSystem, _currentImageFullPath).ConfigureAwait(true))
            return;
        var p = _slideshow.GetCurrentPath();
        if (!string.IsNullOrEmpty(p))
            EnqueuePreviewNavigation(p, true);
        UpdateSlideshowScopeBadge();
    }

    private async Task SlideshowDeleteCurrentFromInputAsync()
    {
        if (!_slideshowUiActive || _slideshow == null || string.IsNullOrEmpty(_currentImageFullPath))
            return;

        var target = _currentImageFullPath;
        var warn = new ContentDialog
        {
            Title = "Delete current slideshow image?",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.WrapWholeWords,
                MaxWidth = 440,
                Text = $"Send this file to the Recycle Bin (or permanently delete if the Recycle Bin is unavailable)?\n\n{Path.GetFileName(target)}",
            },
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot,
        };
        if (await ShowWizardContentDialogAsync(warn) != ContentDialogResult.Primary)
            return;

        var hadSiblingOverlay = _slideshow.IsSiblingOverlayActive;
        var treeAnchorPath = _slideshow.Tree.CurrentPath;

        var dir = Path.GetDirectoryName(target);
        var ok = await WizardExecuteImageRecycleOrPermanentBatchAsync(
                new[] { target },
                recordUndoForRecycledPaths: false,
                operationNameForLog: "SlideshowDelete",
                workingFolderOverride: string.IsNullOrEmpty(dir) ? null : dir,
                deferBrowserPaneRefresh: false,
                assumePermanentFallbackForRecycleFailures: true)
            .ConfigureAwait(true);
        if (!ok)
            return;

        var navigated = false;
        _slideshow.ClearSiblingOverlay();

        if (hadSiblingOverlay
            && !string.IsNullOrEmpty(treeAnchorPath)
            && !string.Equals(treeAnchorPath, target, StringComparison.OrdinalIgnoreCase)
            && File.Exists(treeAnchorPath))
        {
            await CommitPreviewImmediatelyAsync(treeAnchorPath).ConfigureAwait(true);
            navigated = true;
        }

        if (!navigated
            && _slideshow.TryMoveNextTree(out var next) && next != null)
        {
            await CommitPreviewImmediatelyAsync(next).ConfigureAwait(true);
            navigated = true;
        }

        if (!navigated)
        {
            SetTransientStatus("No more images in slideshow.");
            if (_isFullscreen)
                ExitFullscreenChrome();
            DiscardSlideshowSession();
        }

        UpdateSlideshowScopeBadge();
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
                    "Tree slideshow walks your folder in the background (no full scan before the first slide). Each Next picks an image uniformly at random from every path discovered so far in this session. Up to 50,000 paths are kept in memory; additional paths use a temporary on-disk list so RAM stays bounded while the draw stays fair. Discovery order among subfolders is shuffled so slides are not stuck in alphabetically first folders. Previous steps back through slides you have seen; Next moves forward through that history again, or picks a new random image when you are at the latest slide. Reshuffle clears the session and starts discovery again. For a future persistent index option, see the product roadmap.",
            },
            CloseButtonText = "OK",
            XamlRoot = RootGrid.XamlRoot,
        };
        await ShowWizardContentDialogAsync(dlg);
    }

    private void SortFlagKeep_Click(object sender, RoutedEventArgs e) => SetSelectedSortFlag(SortFlagState.Keep);

    private void SortFlagDelete_Click(object sender, RoutedEventArgs e) => SetSelectedSortFlag(SortFlagState.Delete);

    private void SortFlagUnset_Click(object sender, RoutedEventArgs e) => SetSelectedSortFlag(SortFlagState.Unset);

    private void SetSelectedSortFlag(SortFlagState state)
    {
        if (!TryGetSortFlagTargetPath(out var path))
            return;
        var current = _sortSession.GetState(path);
        var resolved = SortFlagInput.ResolveToggle(current, state);
        _sortSession.SetState(path, resolved);
        UpdatePathOverlays();
        RefreshSortFlagDisplayInList(path);
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
        if (IsHotkeyChordRecordingActive)
            return;
        if (_slideshowUiActive)
            return;
        if (!TryGetSortFlagTargetPath(out _))
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
