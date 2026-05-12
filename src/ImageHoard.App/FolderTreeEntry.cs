using System.ComponentModel;
using System.Runtime.CompilerServices;

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

    public FolderTreeEntry(string path, string displayLabel)
    {
        _path = path;
        _displayLabel = displayLabel;
        _editingName = displayLabel;
    }

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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public override string ToString() => DisplayLabel;
}
