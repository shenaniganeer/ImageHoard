using System.ComponentModel;
using System.Runtime.CompilerServices;
using ImageHoard.Core.Browse2;
using ImageHoard.Core.Sort;
using Microsoft.UI.Xaml;

namespace ImageHoard.App.BrowserV2;

/// <summary>One virtualized row in <see cref="ImagePaneView"/>.</summary>
public sealed class ImagePaneRow : INotifyPropertyChanged
{
    private Visibility _sizeColumnVisibility = Visibility.Visible;
    private Visibility _dateColumnVisibility = Visibility.Visible;
    private Visibility _sortFlagKeepIconVisibility = Visibility.Collapsed;
    private Visibility _sortFlagDeleteIconVisibility = Visibility.Collapsed;

    public ImagePaneRow(
        string fullPath,
        string displayName,
        long? sizeBytes,
        DateTimeOffset? modifiedUtc,
        Visibility sizeColumnVisibility,
        Visibility dateColumnVisibility,
        SortFlagState sortFlag)
    {
        FullPath = fullPath;
        DisplayName = displayName;
        SizeBytes = sizeBytes;
        ModifiedUtc = modifiedUtc;
        SizeDisplay = sizeBytes == null ? "—" : FolderRowFormatting.FormatSize(sizeBytes.Value);
        DateDisplay = modifiedUtc == null ? "—" : modifiedUtc.Value.ToLocalTime().ToString("g");
        _sizeColumnVisibility = sizeColumnVisibility;
        _dateColumnVisibility = dateColumnVisibility;
        ApplySortFlagCore(sortFlag, notify: false);
    }

    public string FullPath { get; }

    public string DisplayName { get; }

    public long? SizeBytes { get; }

    public DateTimeOffset? ModifiedUtc { get; }

    public string SizeDisplay { get; }

    public string DateDisplay { get; }

    public Visibility SizeColumnVisibility
    {
        get => _sizeColumnVisibility;
        set
        {
            if (_sizeColumnVisibility == value)
                return;
            _sizeColumnVisibility = value;
            OnPropertyChanged();
        }
    }

    public Visibility DateColumnVisibility
    {
        get => _dateColumnVisibility;
        set
        {
            if (_dateColumnVisibility == value)
                return;
            _dateColumnVisibility = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Green chevron (Keep). Foreground is fixed in XAML so rows can be built off the UI thread.</summary>
    public Visibility SortFlagKeepIconVisibility
    {
        get => _sortFlagKeepIconVisibility;
        private set
        {
            if (_sortFlagKeepIconVisibility == value)
                return;
            _sortFlagKeepIconVisibility = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Red X (Delete). Foreground is fixed in XAML.</summary>
    public Visibility SortFlagDeleteIconVisibility
    {
        get => _sortFlagDeleteIconVisibility;
        private set
        {
            if (_sortFlagDeleteIconVisibility == value)
                return;
            _sortFlagDeleteIconVisibility = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Updates list prefix after <see cref="SortFlagState"/> changes (UI thread).</summary>
    internal void ApplySortFlag(SortFlagState state) => ApplySortFlagCore(state, notify: true);

    private void ApplySortFlagCore(SortFlagState state, bool notify)
    {
        Visibility keepVis;
        Visibility deleteVis;
        switch (state)
        {
            case SortFlagState.Keep:
                keepVis = Visibility.Visible;
                deleteVis = Visibility.Collapsed;
                break;
            case SortFlagState.Delete:
                keepVis = Visibility.Collapsed;
                deleteVis = Visibility.Visible;
                break;
            default:
                keepVis = Visibility.Collapsed;
                deleteVis = Visibility.Collapsed;
                break;
        }

        if (!notify)
        {
            _sortFlagKeepIconVisibility = keepVis;
            _sortFlagDeleteIconVisibility = deleteVis;
            return;
        }

        SortFlagKeepIconVisibility = keepVis;
        SortFlagDeleteIconVisibility = deleteVis;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
