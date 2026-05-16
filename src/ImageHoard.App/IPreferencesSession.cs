using ImageHoard.Core.Input;
using Microsoft.UI.Xaml;

namespace ImageHoard.App;

/// <summary>Host surface for the preferences overlay (<see cref="PreferencesPanel"/>) on <see cref="MainWindow"/>.</summary>
public interface IPreferencesSession
{
    double PreviewNavCatchUpLagSeconds { get; }

    double PreviewMinimumDisplaySeconds { get; }

    /// <summary>0 means use Windows default on the image preview; otherwise milliseconds between presses.</summary>
    int PreviewImagePaneMultiClickThresholdMs { get; }

    string? ArchiveRoot { get; }

    void ApplyPreviewNavCatchUpLagSeconds(double value);

    void ApplyPreviewMinimumDisplaySeconds(double value);

    void ApplyPreviewImagePaneMultiClickThresholdMs(double value);

    Task PromptEditArchiveRootAsync(XamlRoot xamlRoot);

    void ClearCaches(bool deleteOperationLog);

    /// <summary>Shipped builtin plus merged profile for the hotkeys editor; null on failure.</summary>
    Task<(InputProfileDocument Builtin, InputProfileDocument Merged)?> LoadHotkeysEditDocumentsAsync();

    void ReloadInputBindingsAfterHotkeysPersist();

    /// <summary>Called when hotkeys chord capture starts or ends; the host suppresses global input while active.</summary>
    void SetHotkeyChordRecordingActive(bool active);
}
