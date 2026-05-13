using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace ImageHoard.App;

/// <summary>
/// Splitter strip between browser and preview; uses WinUI cursor APIs instead of user32 SetCursor.
/// </summary>
public sealed class BrowserPreviewSplitterGrid : Grid
{
    public BrowserPreviewSplitterGrid()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    }
}
