using System.ComponentModel;
using System.Runtime.CompilerServices;
using ImageHoard.Core.Browse2;
using Microsoft.UI.Xaml;

namespace ImageHoard.App.BrowserV2;

/// <summary>One virtualized row in <see cref="ImagePaneView"/>.</summary>
public sealed class ImagePaneRow : INotifyPropertyChanged
{
    private Visibility _sizeColumnVisibility = Visibility.Visible;
    private Visibility _dateColumnVisibility = Visibility.Visible;

    public ImagePaneRow(
        string fullPath,
        string displayName,
        long? sizeBytes,
        DateTimeOffset? modifiedUtc,
        Visibility sizeColumnVisibility,
        Visibility dateColumnVisibility)
    {
        FullPath = fullPath;
        DisplayName = displayName;
        SizeBytes = sizeBytes;
        ModifiedUtc = modifiedUtc;
        SizeDisplay = sizeBytes == null ? "—" : FolderRowFormatting.FormatSize(sizeBytes.Value);
        DateDisplay = modifiedUtc == null ? "—" : modifiedUtc.Value.ToLocalTime().ToString("g");
        _sizeColumnVisibility = sizeColumnVisibility;
        _dateColumnVisibility = dateColumnVisibility;
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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
