using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ImageHoard.App;

public sealed class ImageRow : INotifyPropertyChanged
{
    public ImageRow(
        string fullPath,
        string displayName,
        long sizeBytes,
        DateTimeOffset modifiedUtc,
        string sizeDisplay,
        string modifiedDisplay,
        string sortFlagDisplay)
    {
        FullPath = fullPath;
        DisplayName = displayName;
        SizeBytes = sizeBytes;
        ModifiedUtc = modifiedUtc;
        SizeDisplay = sizeDisplay;
        ModifiedDisplay = modifiedDisplay;
        _sortFlagDisplay = sortFlagDisplay;
    }

    public string FullPath { get; }
    public string DisplayName { get; }
    public long SizeBytes { get; }
    public DateTimeOffset ModifiedUtc { get; }
    public string SizeDisplay { get; }
    public string ModifiedDisplay { get; }

    private string _sortFlagDisplay;

    public string SortFlagDisplay
    {
        get => _sortFlagDisplay;
        set
        {
            if (string.Equals(_sortFlagDisplay, value, StringComparison.Ordinal))
                return;
            _sortFlagDisplay = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
