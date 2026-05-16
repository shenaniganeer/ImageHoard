using System.ComponentModel;
using System.Runtime.CompilerServices;
using ImageHoard.Core.Browse;
using Microsoft.UI.Xaml;

namespace ImageHoard.App;

/// <summary>TreeViewNode content for an inline file-column header row (Name / Size / Date).</summary>
internal sealed class BrowserFileListHeaderMarker : INotifyPropertyChanged
{
    private Visibility _sizeHeaderVisibility = Visibility.Visible;
    private Visibility _dateHeaderVisibility = Visibility.Visible;
    private ListSortKind _activeListSort = ListSortKind.NameNatural;

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

    public ListSortKind ActiveListSort
    {
        get => _activeListSort;
        set
        {
            if (_activeListSort == value)
                return;
            _activeListSort = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NameNaturalSortIndicatorVisibility));
            OnPropertyChanged(nameof(NamePlainSortIndicatorVisibility));
            OnPropertyChanged(nameof(DateSortIndicatorVisibility));
            OnPropertyChanged(nameof(SizeSortIndicatorVisibility));
        }
    }

    public Visibility NameNaturalSortIndicatorVisibility =>
        ActiveListSort == ListSortKind.NameNatural ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NamePlainSortIndicatorVisibility =>
        ActiveListSort == ListSortKind.Name ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DateSortIndicatorVisibility =>
        ActiveListSort == ListSortKind.DateModified ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SizeSortIndicatorVisibility =>
        ActiveListSort == ListSortKind.Size ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
