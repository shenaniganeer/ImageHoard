using Microsoft.UI.Xaml;

namespace ImageHoard.App;

/// <summary>Host surface for <see cref="PreferencesWindow"/> to read and apply persisted settings.</summary>
public interface IPreferencesSession
{
    bool ShowBrowserPane { get; }

    bool ShowFullscreenPath { get; }

    bool ShowOverlayListPosition { get; }

    bool IncludeSubfoldersInList { get; }

    ListSortKind ListSort { get; }

    bool LogDestructiveOperations { get; }

    bool SlideshowAllowDelete { get; }

    string? ArchiveRoot { get; }

    void ApplyShowBrowserPane(bool value);

    void ApplyShowFullscreenPath(bool value);

    void ApplyShowOverlayListPosition(bool value);

    void ApplyIncludeSubfolders(bool value);

    void ApplyListSort(ListSortKind kind);

    void ApplyLogDestructiveOperations(bool value);

    void ApplySlideshowAllowDelete(bool value);

    Task PromptEditArchiveRootAsync(XamlRoot xamlRoot);

    void ClearCaches(bool deleteOperationLog);

    void OpenHotkeysEditor();

    void SyncChromeFromState();
}
