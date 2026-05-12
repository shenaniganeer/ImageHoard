using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace ImageHoard.App;

/// <summary>TreeViewNode content for an inline file-column header row (Name / Size / Date).</summary>
internal sealed class BrowserFileListHeaderMarker : INotifyPropertyChanged
{
    private Visibility _sizeHeaderVisibility = Visibility.Visible;
    private Visibility _dateHeaderVisibility = Visibility.Visible;

    public Visibility SizeHeaderVisibility
    {
        get => _sizeHeaderVisibility;
        set
        {
            if (_sizeHeaderVisibility == value)
                return;
            _sizeHeaderVisibility = value;
            OnPropertyChanged();
        }
    }

    public Visibility DateHeaderVisibility
    {
        get => _dateHeaderVisibility;
        set
        {
            if (_dateHeaderVisibility == value)
                return;
            _dateHeaderVisibility = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
