using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ImageHoard.App;

public sealed partial class DeleteArchiveWizardPanel : UserControl
{
    private MainWindow _owner = null!;
    private bool _wizardUiPrimed;

    public DeleteArchiveWizardPanel() => InitializeComponent();

    internal void Connect(MainWindow owner) => _owner = owner;

    /// <summary>Raised when the user closes the wizard or after a successful move to archive, delete parent folder, or batch delete (non-keepers / marked Delete).</summary>
    public event EventHandler? RequestClose;

    private void WizardRoot_Loaded(object sender, RoutedEventArgs e)
    {
        _wizardUiPrimed = false;
        InverseKeepBeforeArchiveToggle.IsOn = _owner.SessionInverseKeepDeleteBeforeArchiveMove;
        _wizardUiPrimed = true;
        RefreshUndoAndNoticeUi();
        RefreshArchiveTargetDisplay();
    }

    internal void OnOverlayShown()
    {
        RefreshArchiveTargetDisplay();
        _ = RefreshCountsAsync();
    }

    internal void RefreshArchiveTargetDisplay()
    {
        var session = (IPreferencesSession)_owner;
        var path = session.ArchiveRoot;
        if (string.IsNullOrEmpty(path))
        {
            ArchiveTargetPathText.Text = "(not set)";
            ToolTipService.SetToolTip(
                ArchiveTargetRowButton,
                "Click to select archive target folder");
        }
        else
        {
            ArchiveTargetPathText.Text = path;
            ToolTipService.SetToolTip(ArchiveTargetRowButton, path);
        }
    }

    internal void SetFolderPathDisplay(string path) => FolderPathText.Text = path;

    internal void RefreshUndoAndNoticeUi()
    {
        UndoLastDeleteButton.Visibility = _owner.HasWizardImageUndoPending ? Visibility.Visible : Visibility.Collapsed;
        UndoLastDeleteButton.IsEnabled = !_owner.WizardImageUndoInProgress;
        PermanentDeleteInfoBar.IsOpen = _owner.WizardImagePermanentDeleteNoticeVisible;
    }

    private void PermanentDeleteInfoBar_CloseButtonClick(InfoBar sender, object args)
    {
        _owner.DismissWizardPermanentDeleteNotice();
        sender.IsOpen = false;
    }

    internal void ShowWizardOperationInfo(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        void Apply()
        {
            WizardOperationInfoBar.Title = title;
            WizardOperationInfoBar.Message = message;
            WizardOperationInfoBar.Severity = severity;
            WizardOperationInfoBar.IsOpen = true;
        }

        if (DispatcherQueue.HasThreadAccess)
            Apply();
        else
            DispatcherQueue.TryEnqueue(Apply);
    }

    private async Task RefreshCountsAsync()
    {
        var blockSubfolders = await _owner.DeleteArchiveWizardWorkingFolderHasImmediateSubfoldersAsync().ConfigureAwait(true);
        var snap = await _owner.GetDeleteArchiveWizardCountsAsync().ConfigureAwait(true);
        if (snap == null)
        {
            void ApplyNull()
            {
                var canWork = _owner.HasResolvedDeleteArchiveWizardWorkingFolder();
                CountsText.Text = canWork
                    ? "Could not enumerate images in this folder (counts unavailable)."
                    : "Could not load counts (folder may be unavailable or no image path is set).";
                DeleteNonKeepButton.IsEnabled = false;
                DeleteFlaggedOnlyButton.IsEnabled = false;
                MoveToArchiveButton.IsEnabled = canWork && _owner.HasArchiveRootConfigured && !blockSubfolders;
                DeleteFolderButton.IsEnabled = canWork;
                RenameFolderButton.IsEnabled = canWork;
                RefreshUndoAndNoticeUi();
            }

            if (DispatcherQueue.HasThreadAccess)
                ApplyNull();
            else
                DispatcherQueue.TryEnqueue(ApplyNull);
            return;
        }

        void Apply(MainWindow.DeleteArchiveWizardCountSnap s)
        {
            CountsText.Text =
                $"Keep: {s.Keep} · Delete: {s.Delete} · Unset: {s.Unset} · Not Keep: {s.NotKeepCount}";

            var hasImages = s.TotalImages > 0;
            DeleteNonKeepButton.IsEnabled = hasImages;
            DeleteFlaggedOnlyButton.IsEnabled = hasImages;
            DeleteFlaggedOnlyButton.Content =
                s.DeleteFlaggedCount > 0
                    ? $"Delete {s.DeleteFlaggedCount} image(s) marked Delete"
                    : "Delete images marked Delete";
            MoveToArchiveButton.IsEnabled = _owner.HasArchiveRootConfigured && !blockSubfolders;
            DeleteFolderButton.IsEnabled = true;
            RenameFolderButton.IsEnabled = true;
            RefreshUndoAndNoticeUi();
        }

        if (DispatcherQueue.HasThreadAccess)
            Apply(snap);
        else
            DispatcherQueue.TryEnqueue(() => Apply(snap));
    }

    private void InverseKeepBeforeArchiveToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_wizardUiPrimed)
            return;
        if (sender is ToggleSwitch t)
            _owner.SetInverseKeepDeleteBeforeArchiveMove(t.IsOn == true);
    }

    private async void DeleteNonKeep_Click(object sender, RoutedEventArgs e)
    {
        var ok = await _owner.WizardExecuteInverseKeepDeleteAsync().ConfigureAwait(true);
        if (ok)
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
            return;
        }

        await RefreshCountsAsync().ConfigureAwait(true);
    }

    private async void DeleteFlaggedOnly_Click(object sender, RoutedEventArgs e)
    {
        var ok = await _owner.WizardExecuteDeleteFlaggedOnlyAsync().ConfigureAwait(true);
        if (ok)
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
            return;
        }

        await RefreshCountsAsync().ConfigureAwait(true);
    }

    private async void UndoLastDelete_Click(object sender, RoutedEventArgs e)
    {
        await _owner.WizardUndoLastImageDeletesAsync().ConfigureAwait(true);
        await RefreshCountsAsync().ConfigureAwait(true);
    }

    private async void MoveToArchive_Click(object sender, RoutedEventArgs e)
    {
        var ok = await _owner.WizardExecuteMoveWorkingFolderToArchiveAsync().ConfigureAwait(true);
        if (ok)
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
            return;
        }

        await RefreshCountsAsync().ConfigureAwait(true);
    }

    private async void DeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        var ok = await _owner.WizardExecuteDeleteWorkingFolderToRecycleAsync().ConfigureAwait(true);
        if (ok)
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
            return;
        }

        await RefreshCountsAsync().ConfigureAwait(true);
    }

    private void RenameFolder_Click(object sender, RoutedEventArgs e) =>
        _owner.WizardRequestRenameWorkingFolder();

    internal void SetBrowserPaneMutationBlocking(bool blocking) =>
        CloseWizardButton.IsEnabled = !blocking;

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_owner.IsBrowserPaneMutationInProgress)
            return;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    private async void ArchiveTargetRow_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot == null)
            return;
        await ((IPreferencesSession)_owner).PromptEditArchiveRootAsync(XamlRoot).ConfigureAwait(true);
        RefreshArchiveTargetDisplay();
        var canWork = _owner.HasResolvedDeleteArchiveWizardWorkingFolder();
        var blockSubfolders = await _owner.DeleteArchiveWizardWorkingFolderHasImmediateSubfoldersAsync().ConfigureAwait(true);
        MoveToArchiveButton.IsEnabled = canWork && _owner.HasArchiveRootConfigured && !blockSubfolders;
    }
}
