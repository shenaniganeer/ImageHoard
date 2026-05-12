using ImageHoard.Core.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ImageHoard.App;

public sealed partial class MainWindow
{
    bool IPreferencesSession.ShowBrowserPane => _layoutState.ShowBrowserPane;

    bool IPreferencesSession.ShowPathOnOverlayWindowed => _layoutState.ShowPathOnOverlayWindowed;

    bool IPreferencesSession.ShowPathOnOverlayFullscreen => _layoutState.ShowPathOnOverlayFullscreen;

    bool IPreferencesSession.ShowOverlayListPosition => _layoutState.ShowOverlayListPosition;

    bool IPreferencesSession.IncludeSubfoldersInList => _layoutState.IncludeSubfoldersInList;

    ListSortKind IPreferencesSession.ListSort => _layoutState.ListSort;

    bool IPreferencesSession.LogDestructiveOperations => _session.LogDestructiveOperations;

    bool IPreferencesSession.SlideshowAllowDelete => _session.SlideshowAllowDelete;

    double IPreferencesSession.PreviewNavCatchUpLagSeconds => _layoutState.PreviewNavCatchUpLagSeconds;

    string? IPreferencesSession.ArchiveRoot => _session.ArchiveRoot;

    void IPreferencesSession.ApplyShowBrowserPane(bool value)
    {
        _layoutState.ShowBrowserPane = value;
        ShowBrowserPaneToggle.IsChecked = value;
        ApplyPaneVisibility();
        PersistLayout();
        ((IPreferencesSession)this).SyncChromeFromState();
    }

    void IPreferencesSession.ApplyShowPathOnOverlayWindowed(bool value)
    {
        _layoutState.ShowPathOnOverlayWindowed = value;
        ShowPathOnOverlayWindowedToggle.IsChecked = value;
        UpdatePathOverlays();
        PersistLayout();
        ((IPreferencesSession)this).SyncChromeFromState();
    }

    void IPreferencesSession.ApplyShowPathOnOverlayFullscreen(bool value)
    {
        _layoutState.ShowPathOnOverlayFullscreen = value;
        ShowPathOnOverlayFullscreenToggle.IsChecked = value;
        UpdatePathOverlays();
        PersistLayout();
        ((IPreferencesSession)this).SyncChromeFromState();
    }

    void IPreferencesSession.ApplyShowOverlayListPosition(bool value)
    {
        _layoutState.ShowOverlayListPosition = value;
        UpdatePathOverlays();
        PersistLayout();
        ((IPreferencesSession)this).SyncChromeFromState();
    }

    void IPreferencesSession.ApplyIncludeSubfolders(bool value)
    {
        _layoutState.IncludeSubfoldersInList = value;
        IncludeSubfoldersToggle.IsChecked = value;
        PersistLayout();
        if (!string.IsNullOrEmpty(_currentFolderPath))
            _ = RefreshBrowserTreeFromSettingsAsync();
        ((IPreferencesSession)this).SyncChromeFromState();
    }

    void IPreferencesSession.ApplyListSort(ListSortKind kind)
    {
        _layoutState.ListSort = kind;
        PersistLayout();
        if (!string.IsNullOrEmpty(_currentFolderPath))
            _ = RefreshBrowserTreeFromSettingsAsync();
        ((IPreferencesSession)this).SyncChromeFromState();
    }

    void IPreferencesSession.ApplyLogDestructiveOperations(bool value)
    {
        _session.LogDestructiveOperations = value;
        PersistLayout();
    }

    void IPreferencesSession.ApplySlideshowAllowDelete(bool value)
    {
        _session.SlideshowAllowDelete = value;
        PersistLayout();
    }

    void IPreferencesSession.ApplyPreviewNavCatchUpLagSeconds(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return;
        _layoutState.PreviewNavCatchUpLagSeconds = Math.Clamp(value, 0, 5);
        PersistLayout();
    }

    async Task IPreferencesSession.PromptEditArchiveRootAsync(XamlRoot xamlRoot)
    {
        var box = new TextBox { Text = _session.ArchiveRoot ?? "", Width = 420 };
        var dlg = new ContentDialog
        {
            Title = "Archive root",
            Content = box,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = xamlRoot,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary)
            return;
        _session.ArchiveRoot = box.Text?.Trim();
        PersistLayout();
    }

    void IPreferencesSession.ClearCaches(bool deleteOperationLog)
    {
        AppSettingsStore.ClearCaches(deleteOperationLog);
        SetTransientStatus(
            deleteOperationLog ? "Caches and operation log cleared." : "Caches cleared (folder metrics).");
    }

    async Task<(InputProfileDocument Builtin, InputProfileDocument Merged)?> IPreferencesSession.LoadHotkeysEditDocumentsAsync()
    {
        try
        {
            var builtin = InputProfileBootstrap.TryLoadCombinedShippedBuiltin();
            if (builtin == null)
                return null;

            var userJson = File.Exists(AppDataPaths.UserInputOverridesPath)
                ? await File.ReadAllTextAsync(AppDataPaths.UserInputOverridesPath)
                : null;
            var merged = InputProfileMerger.MergeWithUserOverrides(builtin, userJson);
            return (InputProfileMerger.CloneShallow(builtin), merged);
        }
        catch (Exception ex)
        {
            SetTransientStatus("Hotkeys: " + ex.Message);
            return null;
        }
    }

    void IPreferencesSession.ReloadInputBindingsAfterHotkeysPersist() => TryLoadInputProfile();

    void IPreferencesSession.SyncChromeFromState()
    {
        ShowPathOnOverlayWindowedToggle.IsChecked = _layoutState.ShowPathOnOverlayWindowed;
        ShowPathOnOverlayFullscreenToggle.IsChecked = _layoutState.ShowPathOnOverlayFullscreen;
        ShowBrowserPaneToggle.IsChecked = _layoutState.ShowBrowserPane;
        IncludeSubfoldersToggle.IsChecked = _layoutState.IncludeSubfoldersInList;
        UpdateSortMenuChecks();
    }
}
