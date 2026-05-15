using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ImageHoard.App;

public sealed partial class MainWindow
{
    internal bool IsDeleteArchiveWizardOverlayOpen =>
        DeleteArchiveWizardOverlayRoot.Visibility == Visibility.Visible;

    internal bool IsPreferencesOverlayOpen =>
        PreferencesOverlayRoot.Visibility == Visibility.Visible;

    private void WireModalOverlays()
    {
        DeleteArchiveWizardPanelElement.Connect(this);
        DeleteArchiveWizardPanelElement.RequestClose += (_, _) => HideDeleteArchiveWizardOverlay();
        PreferencesPanelElement.SetHost(this);
        BrowserFindPanelElement.Connect(this);
    }

    /// <summary>When preferences overlay is visible, re-sync toggles from <see cref="MainWindow"/> state.</summary>
    internal void RefreshPreferencesIfVisible()
    {
        if (IsPreferencesOverlayOpen)
            PreferencesPanelElement.RefreshFromHost();
    }

    internal void ShowOrActivatePreferences()
    {
        HideBrowserFindOverlay();
        HideDeleteArchiveWizardOverlay();
        if (IsPreferencesOverlayOpen)
        {
            PreferencesPanelElement.OnOverlayShown();
            PreferencesOverlayRoot.Focus(FocusState.Programmatic);
            return;
        }

        PreferencesOverlayRoot.Visibility = Visibility.Visible;
        PreferencesPanelElement.OnOverlayShown();
        PreferencesOverlayRoot.Focus(FocusState.Programmatic);
    }

    internal void HidePreferencesOverlay()
    {
        PreferencesOverlayRoot.Visibility = Visibility.Collapsed;
        PreferencesPanelElement.OnOverlayHidden();
    }

    internal void HideDeleteArchiveWizardOverlay()
    {
        if (!IsDeleteArchiveWizardOverlayOpen)
            return;
        NotifyDeleteArchiveWizardClosed();
    }

    private void PreferencesOverlayClose_Click(object sender, RoutedEventArgs e) =>
        HidePreferencesOverlay();

    private void PreferencesOverlayRoot_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            if (IsHotkeyChordRecordingActive)
                return;
            HidePreferencesOverlay();
            e.Handled = true;
        }
    }

    private void DeleteArchiveWizardOverlayRoot_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            if (IsBrowserPaneMutationInProgress)
            {
                e.Handled = true;
                return;
            }

            HideDeleteArchiveWizardOverlay();
            e.Handled = true;
        }
    }

    /// <summary>Escape: dismiss topmost overlay (find, then preferences, then wizard). Returns true if an overlay was dismissed.</summary>
    private bool TryDismissTopModalForEscape()
    {
        if (IsBrowserFindOverlayOpen)
        {
            HideBrowserFindOverlay();
            return true;
        }

        if (IsPreferencesOverlayOpen)
        {
            if (IsHotkeyChordRecordingActive)
                return false;
            HidePreferencesOverlay();
            return true;
        }

        if (IsDeleteArchiveWizardOverlayOpen)
        {
            if (IsBrowserPaneMutationInProgress)
                return true;

            HideDeleteArchiveWizardOverlay();
            return true;
        }

        return false;
    }

    private DeleteArchiveWizardPanel? ActiveDeleteArchiveWizardPanel =>
        IsDeleteArchiveWizardOverlayOpen ? DeleteArchiveWizardPanelElement : null;
}
