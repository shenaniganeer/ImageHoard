using ImageHoard.Core.Input;
using Microsoft.UI.Xaml;

namespace ImageHoard.App;

/// <summary>Host surface for the preferences overlay (<see cref="PreferencesPanel"/>) on <see cref="MainWindow"/>.</summary>
public interface IPreferencesSession
{
    double PreviewNavCatchUpLagSeconds { get; }

    string? ArchiveRoot { get; }

    void ApplyPreviewNavCatchUpLagSeconds(double value);

    Task PromptEditArchiveRootAsync(XamlRoot xamlRoot);

    void ClearCaches(bool deleteOperationLog);

    /// <summary>Shipped builtin plus merged profile for the hotkeys editor; null on failure.</summary>
    Task<(InputProfileDocument Builtin, InputProfileDocument Merged)?> LoadHotkeysEditDocumentsAsync();

    void ReloadInputBindingsAfterHotkeysPersist();

    /// <summary>Called when hotkeys chord capture starts or ends; the host suppresses global input while active.</summary>
    void SetHotkeyChordRecordingActive(bool active);
}
