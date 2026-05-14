using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ImageHoard.App;

internal readonly record struct BrowserFindSearchParameters(
    string Query,
    bool MatchFromStartOfName,
    bool FoldersOnly,
    bool DeepSearch);

public sealed partial class BrowserFindPanel : UserControl
{
    private MainWindow _owner = null!;

    public BrowserFindPanel() => InitializeComponent();

    internal void Connect(MainWindow owner) => _owner = owner;

    internal TextBox QueryTextBox => FindQueryTextBox;

    internal BrowserFindSearchParameters GetBrowserFindSearchParameters() =>
        new(
            FindQueryTextBox.Text,
            MatchFromStartRadio.IsChecked == true,
            FoldersOnlyRadio.IsChecked == true,
            DeepSearchToggle.IsOn);

    internal void OnOverlayShown()
    {
        FindQueryTextBox.Focus(FocusState.Programmatic);
        try
        {
            FindQueryTextBox.SelectAll();
        }
        catch
        {
            // ignore
        }
    }

    internal void OnOverlayHidden()
    {
        FindQueryTextBox.Text = string.Empty;
        SetStatus(string.Empty);
    }

    internal void SetStatus(string text) => StatusText.Text = text;

    internal bool IsFocusInsideQueryTextBox()
    {
        var xamlRoot = XamlRoot;
        if (xamlRoot == null)
            return false;
        var focused = FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;
        return IsDescendantOf(focused, FindQueryTextBox);
    }

    private static bool IsDescendantOf(DependencyObject? node, DependencyObject? ancestor)
    {
        while (node != null)
        {
            if (node == ancestor)
                return true;
            node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node);
        }

        return false;
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        var p = GetBrowserFindSearchParameters();
        await _owner.BrowserFindNavigateAsync(-1, p.Query, p.MatchFromStartOfName, p.FoldersOnly, p.DeepSearch)
            .ConfigureAwait(true);
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        var p = GetBrowserFindSearchParameters();
        await _owner.BrowserFindNavigateAsync(1, p.Query, p.MatchFromStartOfName, p.FoldersOnly, p.DeepSearch)
            .ConfigureAwait(true);
    }

    private async void FindQueryTextBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            var p = GetBrowserFindSearchParameters();
            await _owner.BrowserFindNavigateAsync(1, p.Query, p.MatchFromStartOfName, p.FoldersOnly, p.DeepSearch)
                .ConfigureAwait(true);
            return;
        }

        if (e.Key == VirtualKey.Right)
        {
            var tb = FindQueryTextBox;
            if (tb.SelectionStart == tb.Text.Length && tb.SelectionLength == 0)
            {
                e.Handled = true;
                var p = GetBrowserFindSearchParameters();
                await _owner.BrowserFindNavigateAsync(1, p.Query, p.MatchFromStartOfName, p.FoldersOnly, p.DeepSearch)
                    .ConfigureAwait(true);
            }

            return;
        }

        if (e.Key == VirtualKey.Left)
        {
            var tb = FindQueryTextBox;
            if (tb.SelectionStart == 0 && tb.SelectionLength == 0)
            {
                e.Handled = true;
                var p = GetBrowserFindSearchParameters();
                await _owner.BrowserFindNavigateAsync(-1, p.Query, p.MatchFromStartOfName, p.FoldersOnly, p.DeepSearch)
                    .ConfigureAwait(true);
            }
        }
    }
}
