using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ImageHoard.App;

public sealed partial class MainWindow
{
    bool IPreferencesSession.ShowBrowserPane => _layoutState.ShowBrowserPane;

    bool IPreferencesSession.ShowFullscreenPath => _layoutState.ShowFullscreenPath;

    bool IPreferencesSession.ShowOverlayListPosition => _layoutState.ShowOverlayListPosition;

    bool IPreferencesSession.IncludeSubfoldersInList => _layoutState.IncludeSubfoldersInList;

    ListSortKind IPreferencesSession.ListSort => _layoutState.ListSort;

    bool IPreferencesSession.LogDestructiveOperations => _session.LogDestructiveOperations;

    bool IPreferencesSession.SlideshowAllowDelete => _session.SlideshowAllowDelete;

    string? IPreferencesSession.ArchiveRoot => _session.ArchiveRoot;

    void IPreferencesSession.ApplyShowBrowserPane(bool value)
    {
        _layoutState.ShowBrowserPane = value;
        ShowBrowserPaneToggle.IsChecked = value;
        ApplyPaneVisibility();
        PersistLayout();
        ((IPreferencesSession)this).SyncChromeFromState();
    }

    void IPreferencesSession.ApplyShowFullscreenPath(bool value)
    {
        _layoutState.ShowFullscreenPath = value;
        ShowPathInFullscreenToggle.IsChecked = value;
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
            _ = LoadImageListForPathAsync(_currentFolderPath);
        ((IPreferencesSession)this).SyncChromeFromState();
    }

    void IPreferencesSession.ApplyListSort(ListSortKind kind)
    {
        _layoutState.ListSort = kind;
        PersistLayout();
        if (!string.IsNullOrEmpty(_currentFolderPath))
            _ = LoadImageListForPathAsync(_currentFolderPath);
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

    void IPreferencesSession.OpenHotkeysEditor() => OpenHotkeysEditor();

    void IPreferencesSession.SyncChromeFromState()
    {
        ShowPathInFullscreenToggle.IsChecked = _layoutState.ShowFullscreenPath;
        ShowBrowserPaneToggle.IsChecked = _layoutState.ShowBrowserPane;
        IncludeSubfoldersToggle.IsChecked = _layoutState.IncludeSubfoldersInList;
        UpdateSortMenuChecks();
    }
}
