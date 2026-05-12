using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace ImageHoard.App;

public sealed partial class PreferencesWindow : Window
{
    private static PreferencesWindow? _instance;
    private IPreferencesSession _host;
    private bool _suppressEvents;

    public PreferencesWindow(IPreferencesSession host)
    {
        InitializeComponent();
        _host = host;
        Closed += (_, _) => _instance = null;
        this.Activated += PreferencesWindow_Activated;
        TryResize();
    }

    private void RootNav_Loaded(object sender, RoutedEventArgs e)
    {
        RootNav.SelectedItem = NavItemGeneral;
        ShowPage("general");
        RefreshFromHost();
    }

    public static void ShowOrActivate(IPreferencesSession host)
    {
        if (_instance != null)
        {
            _instance._host = host;
            _instance.RefreshFromHost();
            _instance.Activate();
            return;
        }

        var w = new PreferencesWindow(host);
        _instance = w;
        w.Activate();
    }

    private void PreferencesWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
            RefreshFromHost();
    }

    private void TryResize()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow.GetFromWindowId(windowId).Resize(new Windows.Graphics.SizeInt32(880, 640));
        }
        catch
        {
            // best-effort
        }
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
    }

    private void RefreshFromHost()
    {
        _suppressEvents = true;
        try
        {
            LogOperationsToggle.IsOn = _host.LogDestructiveOperations;
            ShowBrowserPaneToggle.IsOn = _host.ShowBrowserPane;
            ShowPathOverlayToggle.IsOn = _host.ShowFullscreenPath;
            ShowOverlayListPositionToggle.IsOn = _host.ShowOverlayListPosition;
            IncludeSubfoldersToggle.IsOn = _host.IncludeSubfoldersInList;
            SlideshowAllowDeleteToggle.IsOn = _host.SlideshowAllowDelete;

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

    private void EditHotkeysButton_Click(object sender, RoutedEventArgs e) => _host.OpenHotkeysEditor();

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

    private void ShowPathOverlayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        _host.ApplyShowFullscreenPath(ShowPathOverlayToggle.IsOn);
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

    private void SlideshowAllowDeleteToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;
        _host.ApplySlideshowAllowDelete(SlideshowAllowDeleteToggle.IsOn);
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
