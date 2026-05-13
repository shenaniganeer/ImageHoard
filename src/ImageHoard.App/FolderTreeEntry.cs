using System.ComponentModel;
using System.Runtime.CompilerServices;
using ImageHoard.Core.Models;
using Microsoft.UI.Xaml;

namespace ImageHoard.App;

/// <summary>
/// WinUI TreeViewNode has no Tag; path and label are stored in <see cref="Microsoft.UI.Xaml.Controls.TreeViewNode.Content"/>.
/// </summary>
internal sealed class FolderTreeEntry : INotifyPropertyChanged
{
    private string _path;
    private string _displayLabel;
    private string _editingName = "";
    private bool _isRenaming;
    private DateTimeOffset? _directoryLastWriteUtc;
    private string _modifiedDisplay = "—";
    private long? _aggregateSizeBytes;
    private string _sizeDisplay = "—";
    private int? _imageFileCount;
    private string _imageCountDisplay = "—";
    private Visibility _sizeDetailVisibility = Visibility.Visible;
    private Visibility _imageCountDetailVisibility = Visibility.Visible;
    private Visibility _dateDetailVisibility = Visibility.Visible;

    public FolderTreeEntry(string path, string displayLabel, DateTimeOffset? directoryLastWriteUtc = null)
    {
        _path = path;
        _displayLabel = displayLabel;
        _editingName = displayLabel;
        DirectoryLastWriteUtc = directoryLastWriteUtc;
    }

    public static FolderTreeEntry FromDirectoryEntry(FileSystemEntry directoryEntry) =>
        new(directoryEntry.FullPath, directoryEntry.Name, directoryEntry.LastWriteTimeUtc);

    public string Path
    {
        get => _path;
        set
        {
            if (string.Equals(_path, value, StringComparison.Ordinal))
                return;
            _path = value;
            OnPropertyChanged();
        }
    }

    public string DisplayLabel
    {
        get => _displayLabel;
        set
        {
            if (string.Equals(_displayLabel, value, StringComparison.Ordinal))
                return;
            _displayLabel = value;
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

    public DateTimeOffset? DirectoryLastWriteUtc
    {
        get => _directoryLastWriteUtc;
        set
        {
            if (_directoryLastWriteUtc == value)
                return;
            _directoryLastWriteUtc = value;
            ModifiedDisplay = DisplayFormat.FolderModified(value);
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

    public long? AggregateSizeBytes
    {
        get => _aggregateSizeBytes;
        set
        {
            if (_aggregateSizeBytes == value)
                return;
            _aggregateSizeBytes = value;
            OnPropertyChanged();
        }
    }

    public string SizeDisplay
    {
        get => _sizeDisplay;
        set
        {
            if (string.Equals(_sizeDisplay, value, StringComparison.Ordinal))
                return;
            _sizeDisplay = value;
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

    public int? ImageFileCount
    {
        get => _imageFileCount;
        private set
        {
            if (_imageFileCount == value)
                return;
            _imageFileCount = value;
            OnPropertyChanged();
        }
    }

    public string ImageCountDisplay
    {
        get => _imageCountDisplay;
        private set
        {
            if (string.Equals(_imageCountDisplay, value, StringComparison.Ordinal))
                return;
            _imageCountDisplay = value;
            OnPropertyChanged();
        }
    }

    public Visibility ImageCountDetailVisibility
    {
        get => _imageCountDetailVisibility;
        set
        {
            if (_imageCountDetailVisibility == value)
                return;
            _imageCountDetailVisibility = value;
            OnPropertyChanged();
        }
    }

    public void SetAggregateSize(long bytes)
    {
        AggregateSizeBytes = bytes;
        SizeDisplay = DisplayFormat.ByteSize(bytes);
    }

    public void ClearAggregateSizePending()
    {
        AggregateSizeBytes = null;
        SizeDisplay = "…";
    }

    public void ClearAggregateSizeUnavailable()
    {
        AggregateSizeBytes = null;
        SizeDisplay = "—";
    }

    public void SetImageFileCount(int count)
    {
        ImageFileCount = count;
        ImageCountDisplay = count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
    }

    public void ClearImageCountPending()
    {
        ImageFileCount = null;
        ImageCountDisplay = "…";
    }

    public void ClearImageCountUnavailable()
    {
        ImageFileCount = null;
        ImageCountDisplay = "—";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public override string ToString() => DisplayLabel;
}
