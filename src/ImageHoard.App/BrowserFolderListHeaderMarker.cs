using System.ComponentModel;
using System.Runtime.CompilerServices;
using ImageHoard.Core.Browse;
using Microsoft.UI.Xaml;

namespace ImageHoard.App;

/// <summary>TreeViewNode content for folder-column header row (Name / Size / Images / Date).</summary>
internal sealed class BrowserFolderListHeaderMarker : INotifyPropertyChanged
{
    private Visibility _sizeHeaderVisibility = Visibility.Visible;
    private Visibility _imageCountHeaderVisibility = Visibility.Visible;
    private Visibility _dateHeaderVisibility = Visibility.Visible;
    private FolderListSortKind _activeSort = FolderListSortKind.NameNatural;

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

    public Visibility ImageCountHeaderVisibility
    {
        get => _imageCountHeaderVisibility;
        set
        {
            if (_imageCountHeaderVisibility == value)
                return;
            _imageCountHeaderVisibility = value;
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

    public FolderListSortKind ActiveSort
    {
        get => _activeSort;
        set
        {
            if (_activeSort == value)
                return;
            _activeSort = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NameSortIndicatorVisibility));
            OnPropertyChanged(nameof(SizeSortIndicatorVisibility));
            OnPropertyChanged(nameof(DateSortIndicatorVisibility));
        }
    }

    public Visibility NameSortIndicatorVisibility =>
        ActiveSort == FolderListSortKind.NameNatural ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SizeSortIndicatorVisibility =>
        ActiveSort == FolderListSortKind.AggregateSize ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DateSortIndicatorVisibility =>
        ActiveSort == FolderListSortKind.DateModified ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
