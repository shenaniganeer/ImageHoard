using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace ImageHoard.App;

public sealed partial class PreferencesPanel : UserControl
{
    private const double PreferencesNavPaneTextMargin = 24;
    private const double PreferencesNavPaneListChrome = 40;
    private const double PreferencesNavPaneMinOpenLength = 96;

    private IPreferencesSession _host = null!;
    private bool _suppressEvents;
    private bool _hotkeysTabPrimed;

    public PreferencesPanel() => InitializeComponent();

    internal void SetHost(IPreferencesSession host) => _host = host;

    /// <summary>Raised when the user dismisses preferences (e.g. Close on hotkeys tab).</summary>
    public event EventHandler? RequestDismiss;

    internal void OnOverlayShown()
    {
        RefreshFromHost();
        _ = DispatcherQueue.GetForCurrentThread().TryEnqueue(
            DispatcherQueuePriority.Low,
            ApplyMenuPaneWidthFromTitles);
    }

    internal void OnOverlayHidden() =>
        HotkeysEditor.RequestDismissPreferences = null;

    private void RootNav_Loaded(object sender, RoutedEventArgs e)
    {
        RootNav.SelectedItem = NavItemGeneral;
        ShowPage("general");
        RefreshFromHost();

        _ = DispatcherQueue.GetForCurrentThread().TryEnqueue(
            DispatcherQueuePriority.Low,
            ApplyMenuPaneWidthFromTitles);
    }

    private void ApplyMenuPaneWidthFromTitles()
    {
        RootNav.UpdateLayout();
        var maxTextWidth = 0.0;
        foreach (var item in RootNav.MenuItems)
        {
            if (item is not NavigationViewItem nvi)
                continue;
            var title = nvi.Content as string ?? nvi.Content?.ToString() ?? string.Empty;
            maxTextWidth = Math.Max(maxTextWidth, MeasureNavigationPaneTitleWidth(title));
        }

        RootNav.OpenPaneLength = Math.Max(
            PreferencesNavPaneMinOpenLength,
            maxTextWidth + PreferencesNavPaneTextMargin + PreferencesNavPaneListChrome);
    }

    private double MeasureNavigationPaneTitleWidth(string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            XamlRoot = RootNav.XamlRoot,
        };

        if (Application.Current?.Resources.TryGetValue("BodyTextBlockStyle", out var bodyResource) == true
            && bodyResource is Style bodyStyle)
            tb.Style = bodyStyle;

        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return tb.DesiredSize.Width;
    }

    private void RootNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
            ShowPage(tag);
    }

    private void ShowPage(string tag)
    {
        PageGeneral.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
        PageHotkeys.Visibility = tag == "hotkeys" ? Visibility.Visible : Visibility.Collapsed;
        PageLibrary.Visibility = tag == "library" ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "hotkeys")
            _ = EnsureHotkeysEditorLoadedAsync();
    }

    internal void RefreshFromHost()
    {
        _suppressEvents = true;
        try
        {
            LogOperationsToggle.IsOn = _host.LogDestructiveOperations;
            ShowBrowserPaneToggle.IsOn = _host.ShowBrowserPane;
            ShowPathOnOverlayWindowedToggle.IsOn = _host.ShowPathOnOverlayWindowed;
            ShowPathOnOverlayFullscreenToggle.IsOn = _host.ShowPathOnOverlayFullscreen;
            ShowOverlayListPositionToggle.IsOn = _host.ShowOverlayListPosition;
            IncludeSubfoldersToggle.IsOn = _host.IncludeSubfoldersInList;
            CollectFolderStatisticsInBackgroundToggle.IsOn = _host.CalculateFolderSizesInBackground;
            SlideshowAllowDeleteToggle.IsOn = _host.SlideshowAllowDelete;
            PreviewNavCatchUpLagNumberBox.Value = _host.PreviewNavCatchUpLagSeconds;

            switch (_host.ListSort)
            {
                case ListSortKind.NameNatural:
                    SortNameNaturalRadio.IsChecked = true;
                    break;
                case ListSortKind.Name:
                    SortNameRadio.IsChecked = true;
                    break;
                case ListSortKind.DateModified:
                    SortDateRadio.IsChecked = true;
                    break;
                case ListSortKind.Size:
                    SortSizeRadio.IsChecked = true;
                    break;
            }

            ArchiveRootDisplay.Text = string.IsNullOrEmpty(_host.ArchiveRoot)
                ? "(not set)"
                : _host.ArchiveRoot;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private async Task EnsureHotkeysEditorLoadedAsync()
    {
        if (_hotkeysTabPrimed)
            return;

        var docs = await _host.LoadHotkeysEditDocumentsAsync();
        if (docs == null)
        {
            HotkeysLoadError.Visibility = Visibility.Visible;
            HotkeysEditor.Visibility = Visibility.Collapsed;
            return;
        }

        HotkeysLoadError.Visibility = Visibility.Collapsed;
        HotkeysEditor.Visibility = Visibility.Visible;
        HotkeysEditor.LoadEditDocumentsAsync = () => _host.LoadHotkeysEditDocumentsAsync();
        HotkeysEditor.BindingsPersisted = () => _host.ReloadInputBindingsAfterHotkeysPersist();
        HotkeysEditor.RequestDismissPreferences = () => RequestDismiss?.Invoke(this, EventArgs.Empty);
        HotkeysEditor.Reset(docs.Value.Builtin, docs.Value.Merged);
        _hotkeysTabPrimed = true;
    }

    private void LogOperationsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        _host.ApplyLogDestructiveOperations(LogOperationsToggle.IsOn);
    }

    private void ShowBrowserPaneToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        _host.ApplyShowBrowserPane(ShowBrowserPaneToggle.IsOn);
    }

    private void ShowPathOnOverlayWindowedToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        _host.ApplyShowPathOnOverlayWindowed(ShowPathOnOverlayWindowedToggle.IsOn);
    }

    private void ShowPathOnOverlayFullscreenToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        _host.ApplyShowPathOnOverlayFullscreen(ShowPathOnOverlayFullscreenToggle.IsOn);
    }

    private void ShowOverlayListPositionToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        _host.ApplyShowOverlayListPosition(ShowOverlayListPositionToggle.IsOn);
    }

    private void IncludeSubfoldersToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        _host.ApplyIncludeSubfolders(IncludeSubfoldersToggle.IsOn);
    }

    private void CollectFolderStatisticsInBackgroundToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        _host.ApplyCalculateFolderSizesInBackground(CollectFolderStatisticsInBackgroundToggle.IsOn);
    }

    private void SlideshowAllowDeleteToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        _host.ApplySlideshowAllowDelete(SlideshowAllowDeleteToggle.IsOn);
    }

    private void PreviewNavCatchUpLagNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressEvents)
            return;
        var v = args.NewValue;
        if (double.IsNaN(v) || double.IsInfinity(v))
            return;
        _host.ApplyPreviewNavCatchUpLagSeconds(v);
    }

    private void SortRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        if (SortNameNaturalRadio.IsChecked == true)
            _host.ApplyListSort(ListSortKind.NameNatural);
        else if (SortNameRadio.IsChecked == true)
            _host.ApplyListSort(ListSortKind.Name);
        else if (SortDateRadio.IsChecked == true)
            _host.ApplyListSort(ListSortKind.DateModified);
        else if (SortSizeRadio.IsChecked == true)
            _host.ApplyListSort(ListSortKind.Size);
    }

    private async void SetArchiveRoot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.XamlRoot == null)
            return;
        await _host.PromptEditArchiveRootAsync(fe.XamlRoot);
        RefreshFromHost();
    }

    private void ClearCaches_Click(object sender, RoutedEventArgs e) =>
        _host.ClearCaches(deleteOperationLog: false);

    private void ClearCachesAndLog_Click(object sender, RoutedEventArgs e) =>
        _host.ClearCaches(deleteOperationLog: true);
}
