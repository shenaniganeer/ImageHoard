using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
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
    private readonly UiLayoutState _layoutState;
    private readonly AppSessionSettings _session;
    private string? _currentImageFullPath;
    private string? _pendingSelectImagePath;
    /// <summary>Last target path for sequential next/prev; avoids relying on TreeView selection updating between rapid commands.</summary>
    private string? _browseNavAnchorPath;

    private BrowseNavigationMode _browseNavigationMode = BrowseNavigationMode.AllImages;

    internal InputKeyboardDispatchTable? KeyboardDispatchTable { get; private set; }

    /// <summary>Keyboard chords for <see cref="BrowserTreeKeyboardCommandIds"/>; matched only while focus is inside <c>FolderTree</c>.</summary>
    internal InputKeyboardDispatchTable? BrowserTreeKeyboardDispatchTable { get; private set; }

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
    private int _populateBrowserGeneration;
    private bool _globalPointerHandlersRegistered;
    private bool _previewPanHandlersRegistered;
    /// <summary>Skips preview clear in <see cref="MainWindow.FolderTree_OnCollapsed"/> during programmatic collapse (e.g. sibling-folder navigation).</summary>
    private bool _suppressFolderTreeCollapsedClear;
    /// <summary>Non-zero while a <see cref="ContentDialog"/> shown from delete/archive wizard flows is active, to ignore spurious tree collapse during modal focus changes.</summary>
    private int _contentDialogModalDepth;
    private PointerEventHandler? _pointerWheelCaptureHandler;
    private PointerEventHandler? _previewScrollContentWheelHandler;
    private PointerEventHandler? _pointerPressedCaptureHandler;
    private PointerEventHandler? _pointerMovedMouseBindingsHandler;

    /// <summary>Non-zero while browser tree / preview context is being reconciled after destructive wizard work (or related paths). Blocks browse-style navigation.</summary>
    private int _browserPaneMutationDepth;

    internal bool IsBrowserPaneMutationInProgress => Volatile.Read(ref _browserPaneMutationDepth) > 0;

    private void EnterBrowserPaneMutation()
    {
        Interlocked.Increment(ref _browserPaneMutationDepth);
        SyncDeleteArchiveWizardBrowserPaneMutationUi();
    }

    private void LeaveBrowserPaneMutation()
    {
        _ = Interlocked.Decrement(ref _browserPaneMutationDepth);
        SyncDeleteArchiveWizardBrowserPaneMutationUi();
    }

    /// <summary>Syncs wizard dismiss controls with <see cref="IsBrowserPaneMutationInProgress"/>.</summary>
    private void SyncDeleteArchiveWizardBrowserPaneMutationUi() =>
        DeleteArchiveWizardPanelElement.SetBrowserPaneMutationBlocking(IsBrowserPaneMutationInProgress);

    private void SetTransientStatus(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return;
        Debug.WriteLine("[ImageHoard] " + message);
        void apply()
        {
            TransientStatusText.Text = message;
        }

        var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dq != null && !dq.HasThreadAccess)
        {
            dq.TryEnqueue(apply);
            return;
        }

        apply();
    }

    public MainWindow()
    {
        (_layoutState, _session) = AppSettingsStore.LoadAll();
        InitializeComponent();
        WireModalOverlays();
        WireBrowserTreeTemplates();
        PreviewHostGrid.SizeChanged += PreviewHostGrid_SizeChanged;
        RootGrid.Loaded += MainWindow_Loaded;
        Closed += (_, _) =>
        {
            if (_persistLayoutDebounceTimer != null)
            {
                _persistLayoutDebounceTimer.Stop();
                _persistLayoutDebounceTimer.Tick -= OnPersistLayoutDebounceTick;
            }

            PersistLayout();
        };
    }

    private static string? GetFolderPath(TreeViewNode? node) =>
        (node?.Content as FolderTreeEntry)?.Path;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLayoutFromState();
        ShowPathOnOverlayWindowedToggle.IsChecked = _layoutState.ShowPathOnOverlayWindowed;
        ShowPathOnOverlayFullscreenToggle.IsChecked = _layoutState.ShowPathOnOverlayFullscreen;
        ShowBrowserPaneToggle.IsChecked = _layoutState.ShowBrowserPane;
        UpdatePathOverlays();
        UpdateFullscreenMenuEnabled();
        SyncBrowseNavigationModeMenu();

        await RestoreStartupBrowseAsync();
        InitializeFeatures();
        UpdateArchiveTargetBrowserRow();
        RebuildBrowseFavoritesMenu();
        RegisterGlobalPointerHandlers();
        RootGrid.Focus(FocusState.Programmatic);
    }

    private async Task RestoreStartupBrowseAsync()
    {
        var path = _session.LastBrowseFolder;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            _browseNavAnchorPath = null;
            FolderTree.RootNodes.Clear();
            UpdateBrowserToolbar();
            return;
        }

        _pendingSelectImagePath = _session.LastSelectedImage;
        await NavigateToFolderAsync(path).ConfigureAwait(true);
    }

    private void RegisterGlobalPointerHandlers()
    {
        if (_globalPointerHandlersRegistered)
            return;
        _globalPointerHandlersRegistered = true;
        _pointerWheelCaptureHandler ??= (_, e) => RootGrid_PointerWheelChanged(_, e);
        RootGrid.AddHandler(UIElement.PointerWheelChangedEvent, _pointerWheelCaptureHandler, handledEventsToo: true);
        _previewScrollContentWheelHandler ??= (_, e) =>
        {
            if (TryDispatchPointerWheelBindings(e))
                e.Handled = true;
        };
        PreviewScrollContentGrid.AddHandler(
            UIElement.PointerWheelChangedEvent,
            _previewScrollContentWheelHandler,
            handledEventsToo: false);
        _pointerPressedCaptureHandler ??= (_, e) => RootGrid_PointerPressed(_, e);
        RootGrid.AddHandler(UIElement.PointerPressedEvent, _pointerPressedCaptureHandler, handledEventsToo: true);
        FullscreenLayout.AddHandler(UIElement.PointerPressedEvent, _pointerPressedCaptureHandler, handledEventsToo: true);
        if (!_previewPanHandlersRegistered)
        {
            _previewPanHandlersRegistered = true;
            RegisterPreviewPanPointerHandlers();
        }

        _pointerMovedMouseBindingsHandler ??= (_, e) => RootGrid_PointerMovedForMouseBindings(_, e);
        RootGrid.AddHandler(UIElement.PointerMovedEvent, _pointerMovedMouseBindingsHandler, handledEventsToo: true);
        FullscreenLayout.AddHandler(UIElement.PointerMovedEvent, _pointerMovedMouseBindingsHandler, handledEventsToo: true);
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
        UpdatePathOverlays();
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _persistLayoutDebounceTimer;

    private void PersistLayout()
    {
        SyncSharesFromGridDefinitions();
        AppSettingsStore.SaveAll(_layoutState, _session);
    }

    private void SchedulePersistLayoutDebounced()
    {
        var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _persistLayoutDebounceTimer ??= dq.CreateTimer();
        _persistLayoutDebounceTimer.Interval = TimeSpan.FromMilliseconds(320);
        _persistLayoutDebounceTimer.IsRepeating = false;
        _persistLayoutDebounceTimer.Tick -= OnPersistLayoutDebounceTick;
        _persistLayoutDebounceTimer.Tick += OnPersistLayoutDebounceTick;
        _persistLayoutDebounceTimer.Stop();
        _persistLayoutDebounceTimer.Start();
    }

    private void OnPersistLayoutDebounceTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        sender.Tick -= OnPersistLayoutDebounceTick;
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

    private ImageRow CreateImageRowFromEntry(FileSystemEntry e)
    {
        var size = e.LengthBytes ?? 0;
        var mtime = e.LastWriteTimeUtc ?? DateTimeOffset.MinValue;
        var row = new ImageRow(e.FullPath, e.Name, size, mtime, DisplayFormat.ByteSize(size), mtime.ToLocalTime().ToString("g"), "·");
        ApplySortFlagPresentationToRow(row, e.FullPath);
        ApplyLayoutFileDetailsToImageRow(row);
        return row;
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

    private void UpdateFullscreenMenuEnabled() =>
        EnterFullscreenMenuItem.IsEnabled = GetSelectedImageRow() is not null;

    private void UpdatePathOverlays()
    {
        var path = _currentImageFullPath ?? string.Empty;
        var hasImage = !string.IsNullOrEmpty(path);
        if (!hasImage)
            ClearArchiveOverlayPreviewState();

        var windowedPathDesired = _layoutState.ShowPathOnOverlayWindowed && hasImage;
        var fullscreenPathDesired = _layoutState.ShowPathOnOverlayFullscreen && hasImage;

        NormalPathText.Text = path;
        FullscreenPathText.Text = path;

        var listPositionVisible = _layoutState.ShowOverlayListPosition && hasImage;
        _ = ApplyOverlayListPositionFromTreeAsync();

        var flag = !hasImage ? SortFlagState.Unset : _sortSession.GetState(path);
        ApplyOverlayFlagGlyph(NormalPathFlagIcon, flag);
        ApplyOverlayFlagGlyph(FullscreenPathFlagIcon, flag);
        var flagVisible = flag != SortFlagState.Unset;

        var navModeLineVisible = hasImage && _browseNavigationMode != BrowseNavigationMode.AllImages;
        if (navModeLineVisible)
        {
            var navLabel = BrowseNavigationModeOverlayLabel(_browseNavigationMode);
            NormalNavigationModeText.Text = navLabel;
            FullscreenNavigationModeText.Text = navLabel;
            NormalNavigationModeText.Visibility = Visibility.Visible;
            FullscreenNavigationModeText.Visibility = Visibility.Visible;
        }
        else
        {
            NormalNavigationModeText.Visibility = Visibility.Collapsed;
            FullscreenNavigationModeText.Visibility = Visibility.Collapsed;
        }

        NormalPathText.Visibility = windowedPathDesired ? Visibility.Visible : Visibility.Collapsed;
        FullscreenPathText.Visibility = fullscreenPathDesired ? Visibility.Visible : Visibility.Collapsed;

        var archiveLinesVisible = ApplyArchiveOverlayLines(hasImage);
        EnsureArchiveOverlayPreviewScheduled();

        var showNormalOverlay = hasImage
            && !_isFullscreen
            && (windowedPathDesired || listPositionVisible || flagVisible || navModeLineVisible || archiveLinesVisible);
        var showFullscreenOverlay = hasImage
            && _isFullscreen
            && (fullscreenPathDesired || listPositionVisible || flagVisible || navModeLineVisible || archiveLinesVisible);

        NormalPathOverlay.Visibility = showNormalOverlay ? Visibility.Visible : Visibility.Collapsed;
        FullscreenPathOverlay.Visibility = showFullscreenOverlay ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string BrowseNavigationModeOverlayLabel(BrowseNavigationMode mode) =>
        mode switch
        {
            BrowseNavigationMode.KeepOnly => "Navigation: Keep only",
            BrowseNavigationMode.NotKeepOnly => "Navigation: Not Keep",
            BrowseNavigationMode.UnflaggedOnly => "Navigation: Unflagged only",
            BrowseNavigationMode.DeleteOnly => "Navigation: Delete only",
            _ => "",
        };

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
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        if (folder == null)
            return;

        var folderPath = TryGetStorageFolderPath(folder);
        if (string.IsNullOrEmpty(folderPath))
        {
            SetTransientStatus("Could not resolve folder path for this location.");
            return;
        }

        if (!Directory.Exists(folderPath))
        {
            SetTransientStatus("Folder path not found.");
            return;
        }

        _pendingSelectImagePath = null;
        await NavigateToFolderAsync(folderPath).ConfigureAwait(true);
    }

    private static string? TryGetStorageFolderPath(StorageFolder folder)
    {
        try
        {
            return string.IsNullOrEmpty(folder.Path) ? null : folder.Path;
        }
        catch
        {
            return null;
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Exit();

    private void EnterFullscreen_Click(object sender, RoutedEventArgs e) => TryEnterFullscreen();

    private void ShowPathOnOverlayWindowedToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem t)
        {
            _layoutState.ShowPathOnOverlayWindowed = t.IsChecked == true;
            UpdatePathOverlays();
            PersistLayout();
        }
    }

    private void ShowPathOnOverlayFullscreenToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem t)
        {
            _layoutState.ShowPathOnOverlayFullscreen = t.IsChecked == true;
            UpdatePathOverlays();
            PersistLayout();
        }
    }

    private void BrowseNavModeAll_Click(object sender, RoutedEventArgs e) =>
        SetBrowseNavigationMode(BrowseNavigationMode.AllImages);

    private void BrowseNavModeKeep_Click(object sender, RoutedEventArgs e) =>
        SetBrowseNavigationMode(BrowseNavigationMode.KeepOnly);

    private void BrowseNavModeNotKeep_Click(object sender, RoutedEventArgs e) =>
        SetBrowseNavigationMode(BrowseNavigationMode.NotKeepOnly);

    private void BrowseNavModeUnflagged_Click(object sender, RoutedEventArgs e) =>
        SetBrowseNavigationMode(BrowseNavigationMode.UnflaggedOnly);

    private void BrowseNavModeDelete_Click(object sender, RoutedEventArgs e) =>
        SetBrowseNavigationMode(BrowseNavigationMode.DeleteOnly);

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
        if (GetSelectedImageRow() is null)
            return;
        if (!_isFullscreen)
            ToggleFullscreen();
    }

    private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && TryDismissTopModalForEscape())
        {
            e.Handled = true;
            return;
        }

        if (TryDispatchInputCommand(e))
            return;
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && TryDismissTopModalForEscape())
        {
            e.Handled = true;
            return;
        }

        if (!e.Handled && TryDispatchInputCommand(e))
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

        var deferAppShortcuts = ShouldDeferAppKeyboardShortcuts();
        if (!deferAppShortcuts && e.Key == VirtualKey.F2 && TryBeginRenameSelectedBrowserItem())
        {
            e.Handled = true;
            return;
        }

        if (!deferAppShortcuts && e.Key == VirtualKey.Enter && IsFocusInsideBrowserTree())
        {
            TryEnterFullscreen();
            e.Handled = true;
        }

        if (!deferAppShortcuts)
            HandleSortKeyboardShortcuts(e);
    }

    /// <summary>When true, root-level browse/sort shortcuts must not run so standard text editing (inline rename, etc.) receives keys.</summary>
    private bool ShouldDeferAppKeyboardShortcuts()
    {
        var xamlRoot = RootGrid.XamlRoot;
        if (xamlRoot != null)
        {
            var focused = FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;
            if (IsInsideTextInput(focused))
                return true;
        }

        return _renameTargetNode != null;
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
        IsDescendantOf(source, FolderTree);

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
        if (commandId is "sort.flagKeep" or "sort.flagDelete" or "sort.flagUnset" or "sort.clearAllFlags")
        {
            if (source == null)
                return false;
            if (IsDescendantOf(source, PreviewHostGrid))
                return true;
            if (_isFullscreen && IsDescendantOf(source, FullscreenImage))
                return true;
            return false;
        }

        if (commandId is "sort.deleteArchiveWizard" or "sort.commitBatchDelete" or "sort.moveToArchive")
            return !IsInsideTextInput(source);

        if (commandId == ViewPanPreviewCommandId)
        {
            return source != null && IsDescendantOf(source, PreviewHostGrid);
        }

        return true;
    }

    private void RootGrid_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var origin = e.OriginalSource as DependencyObject;
        if (IsDescendantOf(origin, PreviewScrollContentGrid))
            return;

        if (TryDispatchPointerWheelBindings(e))
            e.Handled = true;
    }

    /// <summary>
    /// Tries merged profile mouse-wheel bindings. Preview content dispatches from
    /// <see cref="PreviewScrollContentGrid"/> so the routed event can be marked handled before
    /// the preview <see cref="Microsoft.UI.Xaml.Controls.ScrollViewer"/> scrolls.
    /// </summary>
    private bool TryDispatchPointerWheelBindings(PointerRoutedEventArgs e)
    {
        var merged = MergedInputProfile;
        if (merged?.Bindings == null)
            return false;

        var suppressNav = ShouldSuppressWheelNavForOriginalSource(e.OriginalSource as DependencyObject);
        var delta = e.GetCurrentPoint(null).Properties.MouseWheelDelta;
        if (delta == 0)
            return false;
        var up = delta > 0;
        var (shift, ctrl, alt, win) = WinUiKeyboardInterop.GetModifierStates();
        var pressedSorted = PointerInputMouseHeldButtons.GetPressedSorted(e.GetCurrentPoint(null).Properties);
        var wheelBindings = InputPointerChordMatch.EnumerateMouseWheelBindings(merged).ToList();

        for (var pass = 1; pass <= 2; pass++)
        {
            foreach (var (commandId, chord) in wheelBindings)
            {
                var specifiesHeld = InputPointerChordMatch.MouseWheelSpecifiesHeldButtons(chord);
                if (pass == 1 && !specifiesHeld)
                    continue;
                if (pass == 2 && specifiesHeld)
                    continue;

                if (!InputPointerChordMatch.IsMouseWheelMatch(chord, shift, ctrl, alt, win, up, pressedSorted))
                    continue;
                if (suppressNav && (commandId == "nav.nextImage" || commandId == "nav.prevImage"))
                    continue;
                if (TryExecuteInputCommand(commandId))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Dispatches <see cref="InputPointerChordMatch.IsMouseButtonMatch"/> for profile bindings.
    /// Routed <see cref="UIElement.PointerPressed"/> may not reach <see cref="RootGrid"/> when a child marks the event handled.
    /// </summary>
    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var refEl = sender as UIElement ?? RootGrid;
        TryDispatchMousePointerBindings(e, refEl, allowPreviewPanBegin: true);
    }

    /// <summary>
    /// Additional mouse buttons while a button is already held are reported on <see cref="UIElement.PointerMoved"/>, not <see cref="UIElement.PointerPressed"/>.
    /// </summary>
    private void RootGrid_PointerMovedForMouseBindings(object sender, PointerRoutedEventArgs e)
    {
        var refEl = sender as UIElement ?? RootGrid;
        var props = e.GetCurrentPoint(refEl).Properties;
        if (MapPointerUpdateKindToSchemaButton(props.PointerUpdateKind) == null)
            return;
        TryDispatchMousePointerBindings(e, refEl, allowPreviewPanBegin: false);
    }

    private void TryDispatchMousePointerBindings(PointerRoutedEventArgs e, UIElement refEl, bool allowPreviewPanBegin)
    {
        var merged = MergedInputProfile;
        if (merged?.Bindings == null)
            return;

        var props = e.GetCurrentPoint(refEl).Properties;
        var pressedSorted = PointerInputMouseHeldButtons.GetPressedSorted(props);
        var buttonName = MapPointerUpdateKindToSchemaButton(props.PointerUpdateKind);

        var origin = e.OriginalSource as DependencyObject;
        var (shift, ctrl, alt, win) = WinUiKeyboardInterop.GetModifierStates();
        if (allowPreviewPanBegin && buttonName != null && TryBeginPreviewPan(e, merged, buttonName, shift, ctrl, alt, win, origin))
            return;

        foreach (var (commandId, chord) in InputPointerChordMatch.EnumerateMouseChordBindings(merged))
        {
            if (commandId == ViewPanPreviewCommandId)
                continue;
            if (!InputPointerChordMatch.IsMouseChordMatch(chord, shift, ctrl, alt, win, pressedSorted))
                continue;
            if (!IsPointerChordAllowedForCommand(commandId, origin))
                continue;
            if (TryExecuteInputCommand(commandId))
            {
                e.Handled = true;
                return;
            }
        }

        if (buttonName == null)
            return;

        foreach (var (commandId, chord) in InputPointerChordMatch.EnumerateMouseButtonBindings(merged))
        {
            if (commandId == ViewPanPreviewCommandId)
                continue;
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
        var xamlRoot = RootGrid.XamlRoot;
        if (xamlRoot != null)
        {
            var focused = FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;
            if (IsInsideTextInput(focused))
                return false;
        }

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

    /// <summary>Matches the tree-only keyboard dispatch table when focus is in the browser tree (before global dispatch).</summary>
    private bool TryDispatchBrowserTreeInputCommand(KeyRoutedEventArgs e)
    {
        if (BrowserTreeKeyboardDispatchTable == null)
            return false;
        if (!IsFocusInsideBrowserTree())
            return false;
        if (ShouldDeferAppKeyboardShortcuts())
            return false;

        var mk = WinUiKeyboardInterop.ToMdnPrimaryKey(e.Key);
        if (mk == null)
            return false;
        var state = WinUiKeyboardInterop.GetKeyboardChordState(mk);
        var cmd = BrowserTreeKeyboardDispatchTable.TryMatchFirst(state);
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

        if (IsPreferencesOverlayOpen && commandId is not ("ui.escape" or "settings.open" or "browse.findInTree"))
            return false;
        if (IsDeleteArchiveWizardOverlayOpen
            && commandId is not ("ui.escape" or "settings.open" or "sort.deleteArchiveWizard" or "sort.commitBatchDelete" or "sort.moveToArchive" or "browse.findInTree"))
            return false;
        if (IsBrowserFindOverlayOpen && commandId is not ("ui.escape" or "browse.findInTree"))
            return false;

        if (IsBrowserPaneMutationInProgress
            && commandId is "nav.nextImage" or "nav.prevImage" or "nav.firstImage" or "nav.lastImage"
                or "nav.nextDirectory" or "nav.prevDirectory" or "nav.cycleNavigationMode" or "slideshow.start"
                or BrowserTreeKeyboardCommandIds.TreeNext or BrowserTreeKeyboardCommandIds.TreePrevious
                or BrowserTreeKeyboardCommandIds.TreeExpand or BrowserTreeKeyboardCommandIds.TreeCollapse
                or BrowserTreeKeyboardCommandIds.TreeDelete)
            return true;

        switch (commandId)
        {
            case "nav.nextImage":
                if (_slideshowUiActive && _slideshow != null)
                {
                    if (_slideshow.TryMoveNext(out var np) && np != null)
                        EnqueuePreviewNavigation(np, true);
                }
                else
                    BrowseNavigateByStep(BrowseNavStepKind.Next);
                return true;
            case "nav.prevImage":
                if (_slideshowUiActive && _slideshow != null)
                {
                    if (_slideshow.TryMovePrevious(out var pp) && pp != null)
                        EnqueuePreviewNavigation(pp, true);
                }
                else
                    BrowseNavigateByStep(BrowseNavStepKind.Previous);
                return true;
            case "nav.firstImage":
                if (_slideshowUiActive && _slideshow != null)
                    return false;
                BrowseNavigateByStep(BrowseNavStepKind.First);
                return true;
            case "nav.lastImage":
                if (_slideshowUiActive && _slideshow != null)
                    return false;
                BrowseNavigateByStep(BrowseNavStepKind.Last);
                return true;
            case "nav.nextDirectory":
                if (_slideshowUiActive && _slideshow != null)
                    return false;
                BrowseNavigateSiblingFolderFromInput(1);
                return true;
            case "nav.prevDirectory":
                if (_slideshowUiActive && _slideshow != null)
                    return false;
                BrowseNavigateSiblingFolderFromInput(-1);
                return true;
            case "nav.cycleNavigationMode":
                if (_slideshowUiActive && _slideshow != null)
                    return false;
                CycleBrowseNavigationModeFromInput();
                return true;
            case "ui.fullscreen":
                ToggleFullscreen();
                return true;
            case "ui.escape":
                if (TryDismissTopModalForEscape())
                    return true;
                if (_isFullscreen)
                    ToggleFullscreen();
                else
                    ClearImageSelectionAndPreview();
                return true;
            case "view.clearSelection":
                if (_isFullscreen)
                    return false;
                ClearImageSelectionAndPreview();
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
            case "browse.findInTree":
                ShowBrowserFindOverlay();
                return true;
            case "slideshow.start":
                SlideshowStart_Click(this, new RoutedEventArgs());
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
                if (!TryGetSortFlagTargetPath(out _))
                    return false;
                SetSelectedSortFlag(SortFlagState.Keep);
                return true;
            case "sort.flagDelete":
                if (!TryGetSortFlagTargetPath(out _))
                    return false;
                SetSelectedSortFlag(SortFlagState.Delete);
                return true;
            case "sort.flagUnset":
                if (!TryGetSortFlagTargetPath(out _))
                    return false;
                SetSelectedSortFlag(SortFlagState.Unset);
                return true;
            case "sort.deleteArchiveWizard":
            case "sort.commitBatchDelete":
            case "sort.moveToArchive":
                ShowOrActivateDeleteArchiveWizard();
                return true;
            case "sort.clearAllFlags":
                SortClearAllFlagsFromInput();
                return true;
            case "settings.clearCaches":
                SettingsClearCaches_Click(this, new RoutedEventArgs());
                return true;
            case "settings.open":
                ShowOrActivatePreferences();
                return true;
            case BrowserTreeKeyboardCommandIds.TreeNext:
                if (!IsFocusInsideBrowserTree())
                    return false;
                BrowseTreeKeyboardMoveSelection(1);
                return true;
            case BrowserTreeKeyboardCommandIds.TreePrevious:
                if (!IsFocusInsideBrowserTree())
                    return false;
                BrowseTreeKeyboardMoveSelection(-1);
                return true;
            case BrowserTreeKeyboardCommandIds.TreeExpand:
                if (!IsFocusInsideBrowserTree())
                    return false;
                BrowseTreeKeyboardExpandFolderTarget();
                return true;
            case BrowserTreeKeyboardCommandIds.TreeCollapse:
                if (!IsFocusInsideBrowserTree())
                    return false;
                BrowseTreeKeyboardCollapseFolderTarget();
                return true;
            case BrowserTreeKeyboardCommandIds.TreeDelete:
                if (!IsFocusInsideBrowserTree())
                    return false;
                QueueExecuteBrowserTreeDeleteFromKeyboardAsync();
                return true;
            default:
                return false;
        }
    }

    private void FullscreenLayout_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_isFullscreen)
            return;

        if (e.Key == VirtualKey.Escape && TryDismissTopModalForEscape())
        {
            e.Handled = true;
            return;
        }

        if (TryDispatchInputCommand(e))
            return;

        if (!e.Handled && !ShouldDeferAppKeyboardShortcuts())
            HandleSortKeyboardShortcuts(e);

        if (TryHandleSlideshowKeys(e))
            return;

        if (e.Key is VirtualKey.F11 or VirtualKey.Enter or VirtualKey.Escape)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    private void ToggleFullscreen()
    {
        var appWindow = GetAppWindow();
        if (!_isFullscreen)
        {
            if (GetSelectedImageRow() is null)
                return;

            appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            _isFullscreen = true;
            NormalLayout.Visibility = Visibility.Collapsed;
            FullscreenLayout.Visibility = Visibility.Visible;
            UpdatePathOverlays();
            FullscreenLayout.Focus(FocusState.Programmatic);
            if (!string.IsNullOrEmpty(_currentImageFullPath) && _fitMode != ImageFitMode.OneToOne)
            {
                InvalidateDecodeTargetTracking();
                _ = ReloadIfDecodeTargetBoxChangedAsync();
            }
        }
        else
        {
            appWindow.SetPresenter(AppWindowPresenterKind.Default);
            _isFullscreen = false;
            FullscreenLayout.Visibility = Visibility.Collapsed;
            NormalLayout.Visibility = Visibility.Visible;
            StopSlideshowSession();
            UpdatePathOverlays();
            RootGrid.Focus(FocusState.Programmatic);
            if (!string.IsNullOrEmpty(_currentImageFullPath) && _fitMode != ImageFitMode.OneToOne)
            {
                InvalidateDecodeTargetTracking();
                _ = ReloadIfDecodeTargetBoxChangedAsync();
            }
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

    private void Splitter_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        EndSplitDrag();
    }

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
