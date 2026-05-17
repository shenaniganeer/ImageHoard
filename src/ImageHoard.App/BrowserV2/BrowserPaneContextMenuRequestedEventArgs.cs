using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace ImageHoard.App.BrowserV2;

/// <summary>Right-click on a Browse2 pane row; host shows the shared browser context <see cref="Microsoft.UI.Xaml.Controls.MenuFlyout"/>.</summary>
public sealed class BrowserPaneContextMenuRequestedEventArgs : EventArgs
{
    public BrowserPaneContextMenuRequestedEventArgs(FrameworkElement anchor, RightTappedRoutedEventArgs source)
    {
        Anchor = anchor;
        Source = source;
    }

    public FrameworkElement Anchor { get; }

    public RightTappedRoutedEventArgs Source { get; }
}
