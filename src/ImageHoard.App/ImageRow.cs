using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ImageHoard.App;

public sealed class ImageRow : INotifyPropertyChanged
{
    private string _fullPath;
    private string _displayName;
    private string _editingName;
    private bool _isRenaming;
    private long _sizeBytes;
    private DateTimeOffset _modifiedUtc;
    private string _sizeDisplay;
    private string _modifiedDisplay;
    private string _sortFlagDisplay;
    private Visibility _sortFlagGlyphVisibility = Visibility.Collapsed;
    private Symbol _sortFlagGlyphSymbol = Symbol.Accept;
    private Brush _sortFlagGlyphForeground = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
    private Visibility _sizeDetailVisibility = Visibility.Visible;
    private Visibility _dateDetailVisibility = Visibility.Visible;

    public ImageRow(
        string fullPath,
        string displayName,
        long sizeBytes,
        DateTimeOffset modifiedUtc,
        string sizeDisplay,
        string modifiedDisplay,
        string sortFlagDisplay)
    {
        _fullPath = fullPath;
        _displayName = displayName;
        _editingName = displayName;
        _sizeBytes = sizeBytes;
        _modifiedUtc = modifiedUtc;
        _sizeDisplay = sizeDisplay;
        _modifiedDisplay = modifiedDisplay;
        _sortFlagDisplay = sortFlagDisplay;
    }

    public string FullPath
    {
        get => _fullPath;
        private set
        {
            if (string.Equals(_fullPath, value, StringComparison.Ordinal))
                return;
            _fullPath = value;
            OnPropertyChanged();
        }
    }

    public string DisplayName
    {
        get => _displayName;
        private set
        {
            if (string.Equals(_displayName, value, StringComparison.Ordinal))
                return;
            _displayName = value;
            OnPropertyChanged();
        }
    }

    public string EditingName
    {
        get => _editingName;
        set
        {
            if (string.Equals(_editingName, value, StringComparison.Ordinal))
                return;
            _editingName = value;
            OnPropertyChanged();
        }
    }

    public bool IsRenaming
    {
        get => _isRenaming;
        set
        {
            if (_isRenaming == value)
                return;
            _isRenaming = value;
            OnPropertyChanged();
        }
    }

    public long SizeBytes
    {
        get => _sizeBytes;
        private set
        {
            if (_sizeBytes == value)
                return;
            _sizeBytes = value;
            OnPropertyChanged();
        }
    }

    public DateTimeOffset ModifiedUtc
    {
        get => _modifiedUtc;
        private set
        {
            if (_modifiedUtc == value)
                return;
            _modifiedUtc = value;
            OnPropertyChanged();
        }
    }

    public string SizeDisplay
    {
        get => _sizeDisplay;
        private set
        {
            if (string.Equals(_sizeDisplay, value, StringComparison.Ordinal))
                return;
            _sizeDisplay = value;
            OnPropertyChanged();
        }
    }

    public string ModifiedDisplay
    {
        get => _modifiedDisplay;
        private set
        {
            if (string.Equals(_modifiedDisplay, value, StringComparison.Ordinal))
                return;
            _modifiedDisplay = value;
            OnPropertyChanged();
        }
    }

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

    public Visibility SizeDetailVisibility
    {
        get => _sizeDetailVisibility;
        set
        {
            if (_sizeDetailVisibility == value)
                return;
            _sizeDetailVisibility = value;
            OnPropertyChanged();
        }
    }

    public Visibility DateDetailVisibility
    {
        get => _dateDetailVisibility;
        set
        {
            if (_dateDetailVisibility == value)
                return;
            _dateDetailVisibility = value;
            OnPropertyChanged();
        }
    }

    public Visibility SortFlagGlyphVisibility
    {
        get => _sortFlagGlyphVisibility;
        internal set
        {
            if (_sortFlagGlyphVisibility == value)
                return;
            _sortFlagGlyphVisibility = value;
            OnPropertyChanged();
        }
    }

    public Symbol SortFlagGlyphSymbol
    {
        get => _sortFlagGlyphSymbol;
        internal set
        {
            if (_sortFlagGlyphSymbol == value)
                return;
            _sortFlagGlyphSymbol = value;
            OnPropertyChanged();
        }
    }

    public Brush SortFlagGlyphForeground
    {
        get => _sortFlagGlyphForeground;
        internal set
        {
            if (ReferenceEquals(_sortFlagGlyphForeground, value))
                return;
            _sortFlagGlyphForeground = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Updates size and modified time from disk (same folder path).</summary>
    internal void ApplyRefreshedFileStats(long sizeBytes, DateTimeOffset modifiedUtc)
    {
        SizeBytes = sizeBytes;
        ModifiedUtc = modifiedUtc;
        SizeDisplay = FormatSizeStatic(sizeBytes);
        ModifiedDisplay = modifiedUtc.ToLocalTime().ToString("g");
    }

    /// <summary>Updates path and display metadata after a successful rename on disk.</summary>
    internal void ApplyRenamedPath(string newFullPath, string newDisplayName, long sizeBytes, DateTimeOffset modifiedUtc)
    {
        FullPath = newFullPath;
        DisplayName = newDisplayName;
        EditingName = newDisplayName;
        SizeBytes = sizeBytes;
        ModifiedUtc = modifiedUtc;
        SizeDisplay = FormatSizeStatic(sizeBytes);
        ModifiedDisplay = modifiedUtc.ToLocalTime().ToString("g");
    }

    private static string FormatSizeStatic(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.#} MB";
    }
}
