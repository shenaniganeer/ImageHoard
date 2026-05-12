using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using ImageHoard.App.Imaging;
using ImageHoard.Core;
using ImageHoard.Core.Browse;
using ImageHoard.Core.Input;
using ImageHoard.Core.Models;
using ImageHoard.Core.Services;
using ImageHoard.Core.Sort;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Windowing;
using Windows.UI;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace ImageHoard.App;

public sealed partial class MainWindow : Window, IPreferencesSession
{
    private const double MinBrowserPx = 160;
    private const double MinPreviewPx = 120;

    private AppWindow? _appWindow;
    private bool _isFullscreen;
    private bool _treeExpansionBusy;
    private readonly UiLayoutState _layoutState;
    private readonly AppSessionSettings _session;
    private string? _currentImageFullPath;
    private string? _pendingSelectImagePath;

    internal InputKeyboardDispatchTable? KeyboardDispatchTable { get; private set; }
    internal InputProfileDocument? MergedInputProfile { get; private set; }

    private enum SplitDragKind
    {
        None,
        BrowserPreview,
    }

    private SplitDragKind _splitDrag = SplitDragKind.None;
    private double _splitPressPrimary;
    private double _initBrowserW;
    private double _initPreviewW;

    private string? _currentFolderPath;
    private int _loadImageListGeneration;
    private bool _globalPointerHandlersRegistered;
    private PointerEventHandler? _pointerWheelCaptureHandler;
    private PointerEventHandler? _pointerPressedCaptureHandler;

    [Conditional("DEBUG")]
    private static void SetTransientStatus(string? message)
    {
        if (!string.IsNullOrEmpty(message))
            Debug.WriteLine("[ImageHoard] " + message);
    }

    public MainWindow()
    {
        (_layoutState, _session) = AppSettingsStore.LoadAll();
        InitializeComponent();
        PreviewHostGrid.SizeChanged += PreviewHostGrid_SizeChanged;
        RootGrid.Loaded += MainWindow_Loaded;
        Closed += (_, _) => PersistLayout();
    }

    private static string? GetFolderPath(TreeViewNode? node) =>
        (node?.Content as FolderTreeEntry)?.Path;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLayoutFromState();
        ShowPathInFullscreenToggle.IsChecked = _layoutState.ShowFullscreenPath;
        ShowBrowserPaneToggle.IsChecked = _layoutState.ShowBrowserPane;
        FilesExpander.IsExpanded = _layoutState.FilesExpanderOpen;
        UpdatePathOverlays();
        UpdateFullscreenMenuEnabled();

        await RestoreStartupBrowseAsync();
        InitializeFeatures();
        RegisterGlobalPointerHandlers();
        RootGrid.Focus(FocusState.Programmatic);
    }

    private void FilesExpander_Expanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        _layoutState.FilesExpanderOpen = true;
        PersistLayout();
    }

    private void FilesExpander_Collapsed(Expander sender, ExpanderCollapsedEventArgs args)
    {
        _layoutState.FilesExpanderOpen = false;
        PersistLayout();
    }

    private async Task RestoreStartupBrowseAsync()
    {
        var path = _session.LastBrowseFolder;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            FolderTree.RootNodes.Clear();
            ImageList.ItemsSource = null;
            return;
        }

        _pendingSelectImagePath = _session.LastSelectedImage;
        SetSingleFolderTreeRoot(path);
        FolderTree.SelectedNode = FolderTree.RootNodes[0];
        await LoadImageListForPathAsync(path);
    }

    internal void SetSingleFolderTreeRoot(string path)
    {
        FolderTree.RootNodes.Clear();
        var root = new TreeViewNode
        {
            Content = new FolderTreeEntry { Path = path, DisplayLabel = path },
        };
        try
        {
            root.HasUnrealizedChildren = Directory.EnumerateDirectories(path).Take(1).Any();
        }
        catch
        {
            root.HasUnrealizedChildren = false;
        }

        FolderTree.RootNodes.Add(root);
    }

    private void RegisterGlobalPointerHandlers()
    {
        if (_globalPointerHandlersRegistered)
            return;
        _globalPointerHandlersRegistered = true;
        _pointerWheelCaptureHandler ??= (_, e) => RootGrid_PointerWheelChanged(_, e);
        RootGrid.AddHandler(UIElement.PointerWheelChangedEvent, _pointerWheelCaptureHandler, handledEventsToo: true);
        FullscreenLayout.AddHandler(UIElement.PointerWheelChangedEvent, _pointerWheelCaptureHandler, handledEventsToo: true);
        _pointerPressedCaptureHandler ??= (_, e) => RootGrid_PointerPressed(_, e);
        RootGrid.AddHandler(UIElement.PointerPressedEvent, _pointerPressedCaptureHandler, handledEventsToo: true);
        FullscreenLayout.AddHandler(UIElement.PointerPressedEvent, _pointerPressedCaptureHandler, handledEventsToo: true);
    }

    private void ApplyLayoutFromState() => ApplyPaneVisibility();

    private void ApplyPaneVisibility()
    {
        const double scale = 100;
        var showBrowser = _layoutState.ShowBrowserPane;
        ColBrowser.MinWidth = showBrowser ? MinBrowserPx : 0;
        ColBrowser.Width = showBrowser
            ? new GridLength(_layoutState.BrowserColumnShare * scale, GridUnitType.Star)
            : new GridLength(0);

        ColGapBrowserPreview.MinWidth = 0;
        ColGapBrowserPreview.Width = showBrowser ? new GridLength(6) : new GridLength(0);

        ColPreview.MinWidth = MinPreviewPx;
        ColPreview.Width = new GridLength(_layoutState.PreviewColumnShare * scale, GridUnitType.Star);

        BrowserColumnHost.Visibility = showBrowser ? Visibility.Visible : Visibility.Collapsed;
        SplitterBrowserPreview.Visibility = showBrowser ? Visibility.Visible : Visibility.Collapsed;
        ImageList.Visibility = Visibility.Visible;
        UpdatePathOverlays();
    }

    private void PersistLayout()
    {
        SyncSharesFromGridDefinitions();
        AppSettingsStore.SaveAll(_layoutState, _session);
    }

    private void SyncSharesFromGridDefinitions()
    {
        var showBrowser = _layoutState.ShowBrowserPane;
        double b = 0, p = 0;
        if (showBrowser && ColBrowser.Width.GridUnitType == GridUnitType.Star)
            b = ColBrowser.Width.Value;
        if (ColPreview.Width.GridUnitType == GridUnitType.Star)
            p = ColPreview.Width.Value;

        var sum = b + p;
        if (sum > 1e-6)
        {
            if (showBrowser)
                _layoutState.BrowserColumnShare = b / sum;
            _layoutState.PreviewColumnShare = p / sum;
        }
    }

    private AppWindow GetAppWindow()
    {
        if (_appWindow != null)
            return _appWindow;
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        return _appWindow;
    }

    private async void FolderTree_OnExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (_treeExpansionBusy)
            return;
        var node = args.Node;
        if (node.Children.Count > 0)
        {
            node.HasUnrealizedChildren = false;
            return;
        }

        var path = GetFolderPath(node);
        if (string.IsNullOrEmpty(path))
            return;

        _treeExpansionBusy = true;
        try
        {
            IReadOnlyList<FileSystemEntry> entries;
            try
            {
                entries = await AppServices.FileSystem.ListDirectoryAsync(path);
            }
            catch (Exception ex)
            {
                SetTransientStatus(ex.Message);
                return;
            }

            foreach (var dir in entries.Where(x => x.IsDirectory))
            {
                var child = new TreeViewNode
                {
                    Content = new FolderTreeEntry { Path = dir.FullPath, DisplayLabel = dir.Name },
                };
                try
                {
                    child.HasUnrealizedChildren = Directory.EnumerateDirectories(dir.FullPath).Take(1).Any();
                }
                catch
                {
                    child.HasUnrealizedChildren = false;
                }

                node.Children.Add(child);
            }

            node.HasUnrealizedChildren = false;
        }
        finally
        {
            _treeExpansionBusy = false;
        }
    }

    private async void FolderTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        var path = GetFolderPath(sender.SelectedNode);
        if (string.IsNullOrEmpty(path))
        {
            ImageList.ItemsSource = null;
            return;
        }

        await LoadImageListForPathAsync(path);
    }

    private async Task LoadImageListForPathAsync(string path)
    {
        if (!string.IsNullOrEmpty(_currentFolderPath)
            && string.Equals(path, _currentFolderPath, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(_pendingSelectImagePath)
            && !string.IsNullOrEmpty(_currentImageFullPath))
        {
            _pendingSelectImagePath = _currentImageFullPath;
        }

        var gen = Interlocked.Increment(ref _loadImageListGeneration);
        _currentFolderPath = path;
        _session.LastBrowseFolder = path;
        try
        {
            List<ImageRow> rows;
            int? flatEntryCount = null;
            if (_layoutState.IncludeSubfoldersInList)
            {
                var paths = new List<string>();
                await foreach (var p in RecursiveImageEnumerator.EnumerateAsync(AppServices.FileSystem, path))
                    paths.Add(p);

                rows = new List<ImageRow>(paths.Count);
                foreach (var p in paths)
                    rows.Add(await CreateImageRowAsync(p, SortFlagLabel(p)));
            }
            else
            {
                var entries = await AppServices.FileSystem.ListDirectoryAsync(path);
                flatEntryCount = entries.Count;
                rows = new List<ImageRow>();
                foreach (var e in entries.Where(x => !x.IsDirectory && ImageExtensions.IsImageFile(x.FullPath)))
                    rows.Add(CreateImageRowFromEntry(e, SortFlagLabel(e.FullPath)));
            }

            rows = ApplyListSort(rows).ToList();
            var status = rows.Count == 0 && flatEntryCount is > 0
                ? $"0 image(s) · {flatEntryCount} item(s) in folder (none match supported raster extensions)"
                : $"{rows.Count} image(s)";
            ApplyLoadImageListUi(gen, rows, status);
        }
        catch (Exception ex)
        {
            if (gen != Volatile.Read(ref _loadImageListGeneration))
                return;
            ApplyLoadImageListErrorUi(gen, ex.Message);
        }
    }

    private void ApplyLoadImageListUi(int generation, List<ImageRow> rows, string statusText)
    {
        void apply()
        {
            if (generation != Volatile.Read(ref _loadImageListGeneration))
                return;
            ImageList.ItemsSource = new ObservableCollection<ImageRow>(rows);
            SetTransientStatus(statusText);

            var selectPath = _pendingSelectImagePath;
            _pendingSelectImagePath = null;

            var index = 0;
            if (!string.IsNullOrEmpty(selectPath))
            {
                var i = rows.FindIndex(r =>
                    string.Equals(r.FullPath, selectPath, StringComparison.OrdinalIgnoreCase));
                if (i >= 0)
                    index = i;
            }
            else if (!_layoutState.ShowBrowserPane && rows.Count > 0)
            {
                index = 0;
            }

            if (rows.Count > 0)
                ImageList.SelectedIndex = index;

            UpdatePathOverlays();
        }

        if (DispatcherQueue.HasThreadAccess)
            apply();
        else
            _ = DispatcherQueue.TryEnqueue(apply);
    }

    private void ApplyLoadImageListErrorUi(int generation, string message)
    {
        void apply()
        {
            if (generation != Volatile.Read(ref _loadImageListGeneration))
                return;
            SetTransientStatus(message);
            ImageList.ItemsSource = null;
            UpdatePathOverlays();
        }

        if (DispatcherQueue.HasThreadAccess)
            apply();
        else
            _ = DispatcherQueue.TryEnqueue(apply);
    }

    private ImageRow CreateImageRowFromEntry(FileSystemEntry e, string sortFlag)
    {
        var size = e.LengthBytes ?? 0;
        var mtime = e.LastWriteTimeUtc ?? DateTimeOffset.MinValue;
        return new ImageRow(e.FullPath, e.Name, size, mtime, FormatSize(size), mtime.ToLocalTime().ToString("g"), sortFlag);
    }

    private async Task<ImageRow> CreateImageRowAsync(string fullPath, string sortFlag)
    {
        await Task.Yield();
        var fi = new FileInfo(fullPath);
        var size = fi.Exists ? fi.Length : 0;
        var mtime = fi.Exists ? new DateTimeOffset(fi.LastWriteTimeUtc) : DateTimeOffset.UtcNow;
        return new ImageRow(fullPath, fi.Name, size, mtime, FormatSize(size), mtime.ToLocalTime().ToString("g"), sortFlag);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.#} MB";
    }

    private IEnumerable<ImageRow> ApplyListSort(IEnumerable<ImageRow> rows) =>
        _layoutState.ListSort switch
        {
            ListSortKind.NameNatural => rows.OrderBy(r => r.DisplayName, NaturalStringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.FullPath, StringComparer.OrdinalIgnoreCase),
            ListSortKind.Name => rows.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.FullPath, StringComparer.OrdinalIgnoreCase),
            ListSortKind.DateModified => rows.OrderByDescending(r => r.ModifiedUtc).ThenBy(r => r.FullPath, StringComparer.OrdinalIgnoreCase),
            ListSortKind.Size => rows.OrderByDescending(r => r.SizeBytes).ThenBy(r => r.FullPath, StringComparer.OrdinalIgnoreCase),
            _ => rows.OrderBy(r => r.DisplayName, NaturalStringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.FullPath, StringComparer.OrdinalIgnoreCase),
        };

    private void RefreshSortFlagDisplayInList(string fullPath)
    {
        if (ImageList.ItemsSource is not ObservableCollection<ImageRow> rows)
            return;
        for (var i = 0; i < rows.Count; i++)
        {
            if (!string.Equals(rows[i].FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                continue;
            rows[i].SortFlagDisplay = SortFlagLabel(fullPath);
            return;
        }
    }

    private async void ImageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ImageList.SelectedItem is not ImageRow row)
        {
            PreviewImage.Source = null;
            FullscreenImage.Source = null;
            _currentImageFullPath = null;
            _lastDecodeTargetBoxMaxSidePx = -1;
            _session.LastSelectedImage = null;
            UpdatePathOverlays();
            UpdateFullscreenMenuEnabled();
            return;
        }

        _session.LastSelectedImage = row.FullPath;
        UpdateFullscreenMenuEnabled();
        if (PreviewImage.Source != null
            && !string.IsNullOrEmpty(_currentImageFullPath)
            && string.Equals(row.FullPath, _currentImageFullPath, StringComparison.OrdinalIgnoreCase))
        {
            PersistLayout();
            return;
        }

        SetTransientStatus("Loading preview…");
        var layout = CreateWicDecodeLayout();
        var bmp = await WicBitmapLoader.DecodeWithOrientationAsync(row.FullPath, layout);
        if (bmp == null)
        {
            SetTransientStatus("Preview unavailable (codec or format).");
            PreviewImage.Source = null;
            FullscreenImage.Source = null;
            _currentImageFullPath = row.FullPath;
            _lastDecodeTargetBoxMaxSidePx = -1;
            UpdatePathOverlays();
            PersistLayout();
            return;
        }

        try
        {
            var src = new SoftwareBitmapSource();
            await src.SetBitmapAsync(bmp);
            PreviewImage.Source = src;
            FullscreenImage.Source = src;
            _currentImageFullPath = row.FullPath;
            RememberDecodeTargetBox(layout);
            UpdatePathOverlays();
            SetTransientStatus(row.DisplayName);
        }
        catch
        {
            SetTransientStatus("Preview failed.");
            PreviewImage.Source = null;
            FullscreenImage.Source = null;
            _currentImageFullPath = null;
            _lastDecodeTargetBoxMaxSidePx = -1;
            UpdatePathOverlays();
        }

        PersistLayout();
    }

    private void UpdateFullscreenMenuEnabled() =>
        EnterFullscreenMenuItem.IsEnabled = ImageList.SelectedItem is ImageRow;

    private void UpdatePathOverlays()
    {
        var path = _currentImageFullPath ?? string.Empty;
        var showPathChrome = _layoutState.ShowFullscreenPath && !string.IsNullOrEmpty(path);
        NormalPathText.Text = path;
        FullscreenPathText.Text = path;

        ApplyOverlayListPosition();

        var flag = string.IsNullOrEmpty(path) ? SortFlagState.Unset : _sortSession.GetState(path);
        ApplyOverlayFlagGlyph(NormalPathFlagIcon, flag);
        ApplyOverlayFlagGlyph(FullscreenPathFlagIcon, flag);

        FullscreenPathOverlay.Visibility = showPathChrome && _isFullscreen ? Visibility.Visible : Visibility.Collapsed;
        var showNormalOverlay = showPathChrome && !_layoutState.ShowBrowserPane && !_isFullscreen;
        NormalPathOverlay.Visibility = showNormalOverlay ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyOverlayListPosition()
    {
        void hideBoth()
        {
            NormalPathPositionText.Visibility = Visibility.Collapsed;
            FullscreenPathPositionText.Visibility = Visibility.Collapsed;
        }

        if (!_layoutState.ShowOverlayListPosition)
        {
            hideBoth();
            return;
        }

        var path = _currentImageFullPath;
        if (string.IsNullOrEmpty(path)
            || ImageList.ItemsSource is not ObservableCollection<ImageRow> rows
            || rows.Count == 0)
        {
            hideBoth();
            return;
        }

        var index = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            if (!string.Equals(rows[i].FullPath, path, StringComparison.OrdinalIgnoreCase))
                continue;
            index = i;
            break;
        }

        if (index < 0)
        {
            hideBoth();
            return;
        }

        var text = $"{index + 1}/{rows.Count}";
        NormalPathPositionText.Text = text;
        FullscreenPathPositionText.Text = text;
        NormalPathPositionText.Visibility = Visibility.Visible;
        FullscreenPathPositionText.Visibility = Visibility.Visible;
    }

    private static void ApplyOverlayFlagGlyph(SymbolIcon icon, SortFlagState flag)
    {
        switch (flag)
        {
            case SortFlagState.Keep:
                icon.Symbol = Symbol.Accept;
                icon.Foreground = new SolidColorBrush(Color.FromArgb(255, 76, 175, 80));
                icon.Visibility = Visibility.Visible;
                break;
            case SortFlagState.Delete:
                icon.Symbol = Symbol.Cancel;
                icon.Foreground = new SolidColorBrush(Color.FromArgb(255, 232, 17, 35));
                icon.Visibility = Visibility.Visible;
                break;
            default:
                icon.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        foreach (var ext in ImageExtensions.PickerFileTypeExtensions)
            picker.FileTypeFilter.Add(ext);
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file == null)
            return;

        var filePath = TryGetStorageFilePath(file);
        if (string.IsNullOrEmpty(filePath))
        {
            SetTransientStatus("Could not resolve file path for this location.");
            return;
        }

        if (!ImageExtensions.IsImageFile(filePath))
        {
            SetTransientStatus("Please choose a supported image file.");
            return;
        }

        var parent = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
        {
            SetTransientStatus("Could not open parent folder.");
            return;
        }

        _pendingSelectImagePath = filePath;
        SetSingleFolderTreeRoot(parent);
        FolderTree.SelectedNode = FolderTree.RootNodes[0];
        await LoadImageListForPathAsync(parent);
    }

    private static string? TryGetStorageFilePath(StorageFile file)
    {
        try
        {
            return string.IsNullOrEmpty(file.Path) ? null : file.Path;
        }
        catch
        {
            return null;
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Exit();

    private void EnterFullscreen_Click(object sender, RoutedEventArgs e) => TryEnterFullscreen();

    private void ShowPathToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem t)
        {
            _layoutState.ShowFullscreenPath = t.IsChecked == true;
            UpdatePathOverlays();
            PersistLayout();
        }
    }

    private void ShowBrowserPaneToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleMenuFlyoutItem t)
            return;
        _layoutState.ShowBrowserPane = t.IsChecked == true;
        ApplyPaneVisibility();
        PersistLayout();
    }

    private void TryEnterFullscreen()
    {
        if (ImageList.SelectedItem is not ImageRow)
            return;
        if (!_isFullscreen)
            ToggleFullscreen();
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (TryDispatchInputCommand(e))
            return;

        if (e.Key == VirtualKey.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }

        if (_isFullscreen)
        {
            if (e.Key is VirtualKey.Enter or VirtualKey.Escape)
            {
                if (_isFullscreen)
                    ToggleFullscreen();
                e.Handled = true;
            }

            return;
        }

        if (e.Key == VirtualKey.Enter && IsFocusInsideImageList())
        {
            TryEnterFullscreen();
            e.Handled = true;
        }

        HandleSortKeyboardShortcuts(e);
    }

    private static bool IsDescendantOf(DependencyObject? node, DependencyObject? ancestor)
    {
        while (node != null)
        {
            if (node == ancestor)
                return true;
            node = VisualTreeHelper.GetParent(node);
        }

        return false;
    }

    private bool ShouldSuppressWheelNavForOriginalSource(DependencyObject? source) =>
        IsDescendantOf(source, ImageList) || IsDescendantOf(source, FolderTree);

    private static bool IsInsideTextInput(DependencyObject? o)
    {
        while (o != null)
        {
            if (o is TextBox or PasswordBox or RichEditBox or AutoSuggestBox)
                return true;
            o = VisualTreeHelper.GetParent(o);
        }

        return false;
    }

    private bool IsPointerChordAllowedForCommand(string commandId, DependencyObject? source)
    {
        if (commandId is "sort.flagKeep" or "sort.flagDelete" or "sort.flagUnset" or "sort.undoLastFlag")
        {
            if (source == null)
                return false;
            if (IsDescendantOf(source, PreviewHostGrid))
                return true;
            if (_isFullscreen && IsDescendantOf(source, FullscreenImage))
                return true;
            return false;
        }

        if (commandId is "sort.commitBatchDelete" or "sort.moveToArchive")
            return !IsInsideTextInput(source);

        return true;
    }

    private void RootGrid_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var merged = MergedInputProfile;
        if (merged?.Bindings == null)
            return;

        var suppressNav = ShouldSuppressWheelNavForOriginalSource(e.OriginalSource as DependencyObject);
        var delta = e.GetCurrentPoint(null).Properties.MouseWheelDelta;
        if (delta == 0)
            return;
        var up = delta > 0;
        var (shift, ctrl, alt, win) = WinUiKeyboardInterop.GetModifierStates();
        foreach (var (commandId, chord) in InputPointerChordMatch.EnumerateMouseWheelBindings(merged))
        {
            if (!InputPointerChordMatch.IsMouseWheelMatch(chord, shift, ctrl, alt, win, up))
                continue;
            if (suppressNav && (commandId == "nav.nextImage" || commandId == "nav.prevImage"))
                continue;
            if (TryExecuteInputCommand(commandId))
            {
                e.Handled = true;
                return;
            }
        }
    }

    /// <summary>
    /// Dispatches <see cref="InputPointerChordMatch.IsMouseButtonMatch"/> for profile bindings.
    /// Routed <see cref="UIElement.PointerPressed"/> may not reach <see cref="RootGrid"/> when a child marks the event handled.
    /// </summary>
    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var merged = MergedInputProfile;
        if (merged?.Bindings == null)
            return;

        var refEl = sender as UIElement ?? RootGrid;
        var buttonName = MapPointerUpdateKindToSchemaButton(e.GetCurrentPoint(refEl).Properties.PointerUpdateKind);
        if (buttonName == null)
            return;

        var origin = e.OriginalSource as DependencyObject;
        var (shift, ctrl, alt, win) = WinUiKeyboardInterop.GetModifierStates();
        foreach (var (commandId, chord) in InputPointerChordMatch.EnumerateMouseButtonBindings(merged))
        {
            if (!InputPointerChordMatch.IsMouseButtonMatch(chord, buttonName, 1, shift, ctrl, alt, win))
                continue;
            if (!IsPointerChordAllowedForCommand(commandId, origin))
                continue;
            if (TryExecuteInputCommand(commandId))
            {
                e.Handled = true;
                return;
            }
        }
    }

    private static string? MapPointerUpdateKindToSchemaButton(PointerUpdateKind k) =>
        k switch
        {
            PointerUpdateKind.LeftButtonPressed => "Left",
            PointerUpdateKind.RightButtonPressed => "Right",
            PointerUpdateKind.MiddleButtonPressed => "Middle",
            PointerUpdateKind.XButton1Pressed => "X1",
            PointerUpdateKind.XButton2Pressed => "X2",
            _ => null,
        };

    private bool TryDispatchInputCommand(KeyRoutedEventArgs e)
    {
        var mk = WinUiKeyboardInterop.ToMdnPrimaryKey(e.Key);
        if (mk == null)
            return false;
        var state = WinUiKeyboardInterop.GetKeyboardChordState(mk);
        var cmd = KeyboardDispatchTable?.TryMatchFirst(state);
        if (string.IsNullOrEmpty(cmd))
            return false;
        if (!TryExecuteInputCommand(cmd))
            return false;
        e.Handled = true;
        return true;
    }

    internal bool TryExecuteInputCommand(string? commandId)
    {
        if (string.IsNullOrEmpty(commandId))
            return false;

        switch (commandId)
        {
            case "nav.nextImage":
                if (_slideshowUiActive && _slideshow != null)
                {
                    if (_slideshow.TryMoveNext(out var np) && np != null)
                        _ = ShowImagePathAsync(np);
                }
                else
                    SelectNextImage();
                return true;
            case "nav.prevImage":
                if (_slideshowUiActive && _slideshow != null)
                {
                    if (_slideshow.TryMovePrevious(out var pp) && pp != null)
                        _ = ShowImagePathAsync(pp);
                }
                else
                    SelectPreviousImage();
                return true;
            case "nav.firstImage":
                if (_slideshowUiActive && _slideshow != null)
                    return false;
                SelectFirstImage();
                return true;
            case "nav.lastImage":
                if (_slideshowUiActive && _slideshow != null)
                    return false;
                SelectLastImage();
                return true;
            case "ui.fullscreen":
                ToggleFullscreen();
                return true;
            case "ui.escape":
                if (_isFullscreen)
                    ToggleFullscreen();
                return true;
            case "view.cycleFitMode":
                ViewCycleFitFromInput();
                return true;
            case "browse.toggleSubfolderInclusion":
                ToggleIncludeSubfoldersFromInput();
                return true;
            case "browse.openGoToPath":
                BrowseGoToPath_Click(this, new RoutedEventArgs());
                return true;
            case "browse.addBookmark":
                BrowseAddFavorite_Click(this, new RoutedEventArgs());
                return true;
            case "browse.revealInExplorer":
                BrowseRevealInExplorer_Click(this, new RoutedEventArgs());
                return true;
            case "slideshow.toggleScope":
                if (_isFullscreen && _slideshowUiActive && _slideshow != null)
                    _ = SlideshowToggleScopeFromKeysAsync();
                return _isFullscreen && _slideshowUiActive;
            case "slideshow.reshuffle":
                if (_slideshow != null)
                    SlideshowReshuffle_Click(this, new RoutedEventArgs());
                return _slideshow != null;
            case "sort.flagKeep":
                if (_isFullscreen || ImageList.SelectedItem is not ImageRow)
                    return false;
                SetSelectedSortFlag(SortFlagState.Keep);
                return true;
            case "sort.flagDelete":
                if (_isFullscreen || ImageList.SelectedItem is not ImageRow)
                    return false;
                SetSelectedSortFlag(SortFlagState.Delete);
                return true;
            case "sort.flagUnset":
                if (_isFullscreen || ImageList.SelectedItem is not ImageRow)
                    return false;
                SetSelectedSortFlag(SortFlagState.Unset);
                return true;
            case "sort.commitBatchDelete":
                SortBatchDelete_Click(this, new RoutedEventArgs());
                return true;
            case "sort.moveToArchive":
                SortMoveArchive_Click(this, new RoutedEventArgs());
                return true;
            case "sort.undoLastFlag":
                SortUndoLastFlagFromInput();
                return true;
            case "settings.clearCaches":
                SettingsClearCaches_Click(this, new RoutedEventArgs());
                return true;
            case "settings.open":
                PreferencesWindow.ShowOrActivate(this);
                return true;
            default:
                return false;
        }
    }

    private void SelectNextImage()
    {
        if (ImageList.ItemsSource is not ObservableCollection<ImageRow> rows || rows.Count == 0)
            return;
        var i = ImageList.SelectedIndex;
        if (i < 0)
            i = 0;
        else
            i = Math.Min(rows.Count - 1, i + 1);
        ImageList.SelectedIndex = i;
    }

    private void SelectPreviousImage()
    {
        if (ImageList.ItemsSource is not ObservableCollection<ImageRow> rows || rows.Count == 0)
            return;
        var i = ImageList.SelectedIndex;
        if (i <= 0)
            i = 0;
        else
            i--;
        ImageList.SelectedIndex = i;
    }

    private void SelectFirstImage()
    {
        if (ImageList.ItemsSource is not ObservableCollection<ImageRow> rows || rows.Count == 0)
            return;
        ImageList.SelectedIndex = 0;
    }

    private void SelectLastImage()
    {
        if (ImageList.ItemsSource is not ObservableCollection<ImageRow> rows || rows.Count == 0)
            return;
        ImageList.SelectedIndex = rows.Count - 1;
    }

    private void FullscreenLayout_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_isFullscreen)
            return;

        if (TryDispatchInputCommand(e))
            return;

        if (TryHandleSlideshowKeys(e))
            return;

        if (e.Key is VirtualKey.F11 or VirtualKey.Enter or VirtualKey.Escape)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    private void ImageList_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_isFullscreen)
            return;

        if (TryDispatchInputCommand(e))
            return;

        if (e.Key == VirtualKey.Enter && ImageList.SelectedItem is ImageRow)
        {
            TryEnterFullscreen();
            e.Handled = true;
        }
    }

    private bool IsFocusInsideImageList()
    {
        var focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot!) as DependencyObject;
        while (focused != null)
        {
            if (focused == ImageList)
                return true;
            focused = VisualTreeHelper.GetParent(focused);
        }

        return false;
    }

    private void ToggleFullscreen()
    {
        var appWindow = GetAppWindow();
        if (!_isFullscreen)
        {
            if (ImageList.SelectedItem is not ImageRow)
                return;

            appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            _isFullscreen = true;
            NormalLayout.Visibility = Visibility.Collapsed;
            FullscreenLayout.Visibility = Visibility.Visible;
            UpdatePathOverlays();
            FullscreenLayout.Focus(FocusState.Programmatic);
        }
        else
        {
            appWindow.SetPresenter(AppWindowPresenterKind.Default);
            _isFullscreen = false;
            FullscreenLayout.Visibility = Visibility.Collapsed;
            NormalLayout.Visibility = Visibility.Visible;
            _slideshowUiActive = false;
            _slideshow?.Tree.StopEnumeration();
            _slideshow = null;
            UpdateSlideshowScopeBadge();
            UpdatePathOverlays();
            RootGrid.Focus(FocusState.Programmatic);
        }
    }

    private void SplitterBrowserPreview_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement el)
            return;
        if (!_layoutState.ShowBrowserPane)
            return;
        _splitDrag = SplitDragKind.BrowserPreview;
        _splitPressPrimary = e.GetCurrentPoint(BrowserPaneGrid).Position.X;
        _initBrowserW = BrowserColumnHost.ActualWidth;
        _initPreviewW = PreviewHostGrid.ActualWidth;
        el.CapturePointer(e.Pointer);
    }

    private void SplitterBrowserPreview_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_splitDrag != SplitDragKind.BrowserPreview || !e.Pointer.IsInContact
            || !_layoutState.ShowBrowserPane)
            return;
        var x = e.GetCurrentPoint(BrowserPaneGrid).Position.X;
        var delta = x - _splitPressPrimary;
        ApplyBrowserPreviewResize(delta);
    }

    private void SplitterBrowserPreview_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_splitDrag != SplitDragKind.BrowserPreview)
            return;
        (sender as UIElement)?.ReleasePointerCapture(e.Pointer);
        EndSplitDrag();
    }

    private void Splitter_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndSplitDrag();

    private void EndSplitDrag()
    {
        if (_splitDrag == SplitDragKind.None)
            return;
        _splitDrag = SplitDragKind.None;
        PersistLayout();
    }

    private void ApplyBrowserPreviewResize(double delta)
    {
        if (!_layoutState.ShowBrowserPane)
            return;
        var newBrowser = _initBrowserW + delta;
        var newPrev = _initPreviewW - delta;
        if (newBrowser < MinBrowserPx)
        {
            newBrowser = MinBrowserPx;
            newPrev = _initBrowserW + _initPreviewW - newBrowser;
        }
        else if (newPrev < MinPreviewPx)
        {
            newPrev = MinPreviewPx;
            newBrowser = _initBrowserW + _initPreviewW - newPrev;
        }

        ApplyContentStarWeights(newBrowser, newPrev);
    }

    private void ApplyContentStarWeights(double browserPx, double previewPx)
    {
        var showBrowser = _layoutState.ShowBrowserPane;
        var b = showBrowser ? Math.Max(1e-3, browserPx) : 0;
        var p = Math.Max(1e-3, previewPx);
        var sum = b + p;
        if (sum < 1e-6)
            return;

        if (showBrowser)
            _layoutState.BrowserColumnShare = b / sum;
        _layoutState.PreviewColumnShare = p / sum;

        ApplyPaneVisibility();
    }
}
