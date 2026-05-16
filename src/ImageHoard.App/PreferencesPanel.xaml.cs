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

    internal void OnOverlayShown()
    {
        RefreshFromHost();
        _ = DispatcherQueue.GetForCurrentThread().TryEnqueue(
            DispatcherQueuePriority.Low,
            ApplyMenuPaneWidthFromTitles);
    }

    internal void OnOverlayHidden()
    {
        _host.SetHotkeyChordRecordingActive(false);
    }

    private void RootNav_Loaded(object sender, RoutedEventArgs e)
    {
        RootNav.SelectedItem = NavItemHotkeys;
        ShowPage("hotkeys");
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
        PageAdvanced.Visibility = tag == "advanced" ? Visibility.Visible : Visibility.Collapsed;
        PageHotkeys.Visibility = tag == "hotkeys" ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "hotkeys")
            _ = EnsureHotkeysEditorLoadedAsync();
    }

    internal void RefreshFromHost()
    {
        _suppressEvents = true;
        try
        {
            PreviewNavCatchUpLagNumberBox.Value = _host.PreviewNavCatchUpLagSeconds;
            PreviewMinimumDisplayNumberBox.Value = _host.PreviewMinimumDisplaySeconds;
            PreviewImagePaneMultiClickThresholdNumberBox.Value = _host.PreviewImagePaneMultiClickThresholdMs;

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
        HotkeysEditor.ChordCaptureActiveChanged = active => _host.SetHotkeyChordRecordingActive(active);
        HotkeysEditor.Reset(docs.Value.Builtin, docs.Value.Merged);
        _hotkeysTabPrimed = true;
        _ = DispatcherQueue.GetForCurrentThread().TryEnqueue(
            DispatcherQueuePriority.Low,
            BumpPagesHostMinHeightFromLayout);
    }

    /// <summary>After Hotkeys rows materialize, grow <see cref="PagesHost"/> min height once so the shell does not jump.</summary>
    private void BumpPagesHostMinHeightFromLayout()
    {
        PagesHost.UpdateLayout();
        var h = PagesHost.ActualHeight;
        if (!double.IsNaN(h) && !double.IsInfinity(h) && h > PagesHost.MinHeight)
            PagesHost.MinHeight = h;
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

    private void PreviewMinimumDisplayNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressEvents)
            return;
        var v = args.NewValue;
        if (double.IsNaN(v) || double.IsInfinity(v))
            return;
        _host.ApplyPreviewMinimumDisplaySeconds(v);
    }

    private void PreviewImagePaneMultiClickThresholdNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressEvents)
            return;
        var v = args.NewValue;
        if (double.IsNaN(v) || double.IsInfinity(v))
            return;
        _host.ApplyPreviewImagePaneMultiClickThresholdMs(v);
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
