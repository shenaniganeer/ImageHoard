using System.IO;
using ImageHoard.Core.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ImageHoard.App;

public sealed partial class MainWindow
{
    double IPreferencesSession.PreviewNavCatchUpLagSeconds => _layoutState.PreviewNavCatchUpLagSeconds;

    string? IPreferencesSession.ArchiveRoot => _session.ArchiveRoot;

    void IPreferencesSession.ApplyPreviewNavCatchUpLagSeconds(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return;
        _layoutState.PreviewNavCatchUpLagSeconds = Math.Clamp(value, 0, 5);
        PersistLayout();
    }

    async Task IPreferencesSession.PromptEditArchiveRootAsync(XamlRoot xamlRoot)
    {
        _ = xamlRoot;
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

        _session.ArchiveRoot = folderPath;
        PersistLayout();
        UpdateArchiveTargetBrowserRow();
        RefreshPreferencesIfVisible();
        ClearArchiveOverlayPreviewState();
        UpdatePathOverlays();
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

    void IPreferencesSession.SetHotkeyChordRecordingActive(bool active) => IsHotkeyChordRecordingActive = active;
}
