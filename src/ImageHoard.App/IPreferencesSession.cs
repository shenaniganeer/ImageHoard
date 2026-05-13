using ImageHoard.Core.Input;
using Microsoft.UI.Xaml;

namespace ImageHoard.App;

/// <summary>Host surface for the preferences overlay (<see cref="PreferencesPanel"/>) on <see cref="MainWindow"/>.</summary>
public interface IPreferencesSession
{
    bool ShowBrowserPane { get; }

    bool ShowPathOnOverlayWindowed { get; }

    bool ShowPathOnOverlayFullscreen { get; }

    bool ShowOverlayListPosition { get; }

    bool IncludeSubfoldersInList { get; }

    bool CalculateFolderSizesInBackground { get; }

    ListSortKind ListSort { get; }

    bool LogDestructiveOperations { get; }

    bool SlideshowAllowDelete { get; }

    double PreviewNavCatchUpLagSeconds { get; }

    string? ArchiveRoot { get; }

    void ApplyShowBrowserPane(bool value);

    void ApplyShowPathOnOverlayWindowed(bool value);

    void ApplyShowPathOnOverlayFullscreen(bool value);

    void ApplyShowOverlayListPosition(bool value);

    void ApplyIncludeSubfolders(bool value);

    void ApplyCalculateFolderSizesInBackground(bool value);

    void ApplyListSort(ListSortKind kind);

    void ApplyLogDestructiveOperations(bool value);

    void ApplySlideshowAllowDelete(bool value);

    void ApplyPreviewNavCatchUpLagSeconds(double value);

    Task PromptEditArchiveRootAsync(XamlRoot xamlRoot);

    void ClearCaches(bool deleteOperationLog);

    /// <summary>Shipped builtin plus merged profile for the hotkeys editor; null on failure.</summary>
    Task<(InputProfileDocument Builtin, InputProfileDocument Merged)?> LoadHotkeysEditDocumentsAsync();

    void ReloadInputBindingsAfterHotkeysPersist();

    void SyncChromeFromState();
}
